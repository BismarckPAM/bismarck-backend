using System.Text;
using System.Text.Json;
using Analytics.Service.Data;
using Confluent.Kafka;
using Messaging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Analytics.Service.Services;

/// <summary>
/// The Analytics Service's own Kafka consumer.
///
/// It follows the same reliability contract as the Audit consumer, but reads
/// under a DIFFERENT consumer group (bismarck-analytics-service). That is
/// essential: consumer groups partition a topic between their members, so reusing
/// the Audit group would split the event stream and Analytics would silently
/// receive only a fraction of the traffic. Separate groups mean both services
/// receive their own full copy while keeping their databases independent.
///
/// Reliability rules implemented here:
/// <list type="number">
///   <item>AutoOffsetReset = Earliest and EnableAutoCommit = false.</item>
///   <item>Idempotency keyed on EventId, backed by a unique index.</item>
///   <item>The database save happens BEFORE the offset commit.</item>
///   <item>On a database/infrastructure failure the offset is left uncommitted so
///         Kafka redelivers the message.</item>
///   <item>Malformed messages are routed to an analytics-specific dead-letter
///         topic and only then committed, so they cannot block the consumer.</item>
/// </list>
/// </summary>
public class KafkaAnalyticsConsumer : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KafkaAnalyticsConsumer> _logger;

    public KafkaAnalyticsConsumer(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<KafkaAnalyticsConsumer> logger)
    {
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var deadLetterTopic = _configuration["Kafka:DeadLetterTopic"] ?? "analytics-dead-letter";
        var groupId = _configuration["Kafka:GroupId"] ?? "bismarck-analytics-service";

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All // Ensure dead-letter messages are safely acknowledged
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();
        using var deadLetterProducer = new ProducerBuilder<Null, string>(producerConfig).Build();

        // Subscribe to every published topic so the analytics database stays
        // extensible and can be reconciled against the full audit history.
        var topics = KafkaTopics.All;

        consumer.Subscribe(topics);
        _logger.LogInformation(
            "KafkaAnalyticsConsumer started with GroupId '{GroupId}'. Subscribed to: {Topics}. DeadLetterTopic: {DeadLetterTopic}",
            groupId,
            string.Join(", ", topics),
            deadLetterTopic);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var consumeResult = consumer.Consume(stoppingToken);
                if (consumeResult?.Message?.Value is null)
                {
                    continue;
                }

                var (secEvent, deserializationError) = TryDeserialize(consumeResult.Message.Value);

                // If invalid/malformed -> forward to the DLQ, then commit.
                if (secEvent is null)
                {
                    await RouteToDeadLetterAsync(
                        deadLetterProducer,
                        deadLetterTopic,
                        consumeResult,
                        deserializationError!,
                        stoppingToken);

                    // Only once it is safely in the DLQ may the offset advance,
                    // otherwise one poison message would stall the consumer forever.
                    consumer.Commit(consumeResult);
                    continue;
                }

                await PersistAsync(consumer, _scopeFactory, secEvent, consumeResult, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("KafkaAnalyticsConsumer cancellation requested. Stopping loop.");
                break;
            }
            catch (Exception ex)
            {
                // Offset is NEVER committed here -> Kafka redelivers the message.
                _logger.LogError(ex, "Error processing event. Offset will NOT be committed. Retrying in 2 seconds...");
                await Task.Delay(2000, stoppingToken);
            }
        }

        try
        {
            // Graceful shutdown: leave the consumer group cleanly.
            consumer.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while closing Kafka consumer.");
        }
    }
    /// <summary>
    /// Deserializes the SecurityEvent envelope. Returns a null event plus a
    /// human-readable reason when the payload is unusable, instead of throwing,
    /// so a poison message is routed to the DLQ rather than stalling the loop.
    /// </summary>
    private static (SecurityEvent<JsonElement>? Event, string? Error) TryDeserialize(string payload)
    {
        try
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var secEvent = JsonSerializer.Deserialize<SecurityEvent<JsonElement>>(payload, jsonOptions);

            if (secEvent is null || secEvent.EventId == Guid.Empty)
            {
                return (null, "Event payload deserialized to null or contains an empty EventId.");
            }

            return (secEvent, null);
        }
        catch (JsonException ex)
        {
            return (null, $"Malformed JSON: {ex.Message}");
        }
    }

    /// <summary>
    /// Persists a valid event idempotently and commits its Kafka offset only
    /// after the database write has succeeded.
    /// </summary>
    private async Task PersistAsync(
        IConsumer<Ignore, string> consumer,
        IServiceScopeFactory dbContextFactory,
        SecurityEvent<JsonElement> secEvent,
        ConsumeResult<Ignore, string> consumeResult,
        CancellationToken stoppingToken)
    {
        using var scope = dbContextFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        // Idempotency: skip if this EventId is already stored.
        bool alreadyExists = await dbContext.AnalyticsEvents
            .AnyAsync(e => e.EventId == secEvent.EventId, stoppingToken);

        if (alreadyExists)
        {
            _logger.LogWarning(
                "Duplicate event detected (EventId: {EventId}). Skipping insert and committing offset.",
                secEvent.EventId);
            consumer.Commit(consumeResult);
            return;
        }

        var analyticsEvent = AnalyticsEventProcessor.Create(
            secEvent,
            consumeResult.Topic,
            DateTimeOffset.UtcNow);

        dbContext.AnalyticsEvents.Add(analyticsEvent);

        try
        {
            await dbContext.SaveChangesAsync(stoppingToken);
        }
        catch (DbUpdateException ex)
        {
            // A unique-constraint violation means a concurrent delivery of the
            // same event won the race. That is safe to absorb.
            if (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                _logger.LogWarning(
                    "EventId {EventId} was inserted by another consumer instance. Skipping duplicate.",
                    secEvent.EventId);
            }
            else
            {
                // Any other database outage (connection timeout, server restart,
                // network break) MUST rethrow so the offset stays UNCOMMITTED and
                // Kafka retries the message.
                _logger.LogError(
                    ex,
                    "Database save failed due to infrastructure or database outage for EventId {EventId}. Leaving offset uncommitted.",
                    secEvent.EventId);
                throw;
            }
        }

        // Commit strictly AFTER a successful save or a confirmed duplicate.
        consumer.Commit(consumeResult);

        _logger.LogInformation(
            "Successfully processed and committed analytics event {EventId} of type {EventType} from topic {Topic}",
            secEvent.EventId,
            secEvent.EventType,
            consumeResult.Topic);
    }

    /// <summary>
    /// Publishes an unparseable message to the analytics dead-letter topic with
    /// the context needed to triage it, before its source offset is committed.
    /// </summary>
    private async Task RouteToDeadLetterAsync(
        IProducer<Null, string> deadLetterProducer,
        string deadLetterTopic,
        ConsumeResult<Ignore, string> consumeResult,
        string errorReason,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Routing malformed message from topic {Topic} to DLQ '{DeadLetterTopic}'. Reason: {Reason}",
            consumeResult.Topic,
            deadLetterTopic,
            errorReason);

        var dltHeaders = new Headers
        {
            { "x-original-topic", Encoding.UTF8.GetBytes(consumeResult.Topic) },
            { "x-original-offset", Encoding.UTF8.GetBytes(consumeResult.Offset.Value.ToString()) },
            { "x-exception-message", Encoding.UTF8.GetBytes(errorReason) }
        };

        await deadLetterProducer.ProduceAsync(deadLetterTopic, new Message<Null, string>
        {
            Value = consumeResult.Message.Value,
            Headers = dltHeaders
        }, cancellationToken);
    }
}
