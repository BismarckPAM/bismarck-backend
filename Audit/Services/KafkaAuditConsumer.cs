using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Audit.Service.Data;
using Audit.Service.Models;
using Messaging;

namespace Audit.Service.Services;

public class KafkaAuditConsumer : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KafkaAuditConsumer> _logger;

    public KafkaAuditConsumer(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<KafkaAuditConsumer> logger)
    {
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var deadLetterTopic = _configuration["Kafka:DeadLetterTopic"] ?? "audit-dead-letter";

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = _configuration["Kafka:GroupId"] ?? "bismarck-audit-service",
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

        var topics = new[]
        {
            KafkaTopics.AccessRequested,
            KafkaTopics.AccessGranted,
            KafkaTopics.AccessDenied,
            KafkaTopics.ApprovalRequested,
            KafkaTopics.ApprovalGranted,
            KafkaTopics.ApprovalRejected,
            KafkaTopics.PermissionRevoked
        };

        consumer.Subscribe(topics);
        _logger.LogInformation("KafkaAuditConsumer started with GroupId '{GroupId}'. Subscribed to: {Topics}", 
            consumerConfig.GroupId, string.Join(", ", topics));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Consume event
                var consumeResult = consumer.Consume(stoppingToken);
                if (consumeResult?.Message?.Value is null)
                {
                    continue;
                }

                // Deserialize event with Dead-Letter Handling
                SecurityEvent<JsonElement>? secEvent = null;
                string? deserializationError = null;

                try
                {
                    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    secEvent = JsonSerializer.Deserialize<SecurityEvent<JsonElement>>(consumeResult.Message.Value, jsonOptions);

                    if (secEvent is null || secEvent.EventId == Guid.Empty)
                    {
                        deserializationError = "Event payload deserialized to null or contains an empty EventId.";
                    }
                }
                catch (JsonException ex)
                {
                    deserializationError = $"Malformed JSON: {ex.Message}";
                }

                // If invalid/malformed -> forward to DEAD-LETTER TOPIC and then commit
                if (deserializationError is not null)
                {
                    _logger.LogWarning("Routing malformed message from topic {Topic} to DLQ '{DeadLetterTopic}'. Reason: {Reason}", 
                        consumeResult.Topic, deadLetterTopic, deserializationError);

                    var dltHeaders = new Headers
                    {
                        { "x-original-topic", Encoding.UTF8.GetBytes(consumeResult.Topic) },
                        { "x-original-offset", Encoding.UTF8.GetBytes(consumeResult.Offset.Value.ToString()) },
                        { "x-exception-message", Encoding.UTF8.GetBytes(deserializationError) }
                    };

                    await deadLetterProducer.ProduceAsync(deadLetterTopic, new Message<Null, string>
                    {
                        Value = consumeResult.Message.Value,
                        Headers = dltHeaders
                    }, stoppingToken);

                    // Once safely inside the Dead-Letter Topic, commit the offset so the consumer does not get blocked
                    consumer.Commit(consumeResult);
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

                // Check if EventId already exists in database (Idempotency)
                bool alreadyExists = await dbContext.AuditLogs
                    .AnyAsync(a => a.EventId == secEvent!.EventId, stoppingToken);

                if (alreadyExists)
                {
                    _logger.LogWarning("Duplicate event detected (EventId: {EventId}). Skipping insert and committing offset.", secEvent!.EventId);
                    consumer.Commit(consumeResult);
                    continue;
                }

                // Insert AuditLog
                var auditLog = new AuditLog
                {
                    Id = Guid.NewGuid(),
                    EventId = secEvent!.EventId,
                    EventType = secEvent.EventType,
                    OccurredAt = secEvent.OccurredAt,
                    Actor = secEvent.Actor,
                    Resource = secEvent.Resource,
                    Action = secEvent.Action,
                    Outcome = secEvent.Outcome,
                    Metadata = secEvent.Metadata.ValueKind != JsonValueKind.Undefined 
                        ? secEvent.Metadata.GetRawText() 
                        : "{}",
                    ConsumedAt = DateTimeOffset.UtcNow
                };

                dbContext.AuditLogs.Add(auditLog);

                // SaveChangesAsync with Strict Outage Handling
                try
                {
                    await dbContext.SaveChangesAsync(stoppingToken);
                }
                catch (DbUpdateException ex)
                {
                    // Strictly check if this is a PostgreSQL Unique Constraint Violation 
                    if (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                    {
                        _logger.LogWarning("EventId {EventId} was inserted by another consumer instance. Skipping duplicate.", secEvent.EventId);
                    }
                    else
                    {
                        // Any other database outage (connection timeout, server reboot, network break)
                        // MUST throw so the offset remains UNCOMMITTED for Kafka to retry
                        _logger.LogError(ex, "Database save failed due to infrastructure or database outage for EventId {EventId}. Leaving offset uncommitted.", secEvent.EventId);
                        throw; 
                    }
                }

                // Commit Kafka offset strictly AFTER save or duplicate confirmation
                consumer.Commit(consumeResult);

                _logger.LogInformation("Successfully processed and committed audit event {EventId} from topic {Topic}", 
                    secEvent.EventId, consumeResult.Topic);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("KafkaAuditConsumer cancellation requested. Stopping loop.");
                break;
            }
            catch (Exception ex)
            {
                // Unhandled DB outage reaches here:
                // Offset is NEVER committed -> Kafka will deliver the message again
                _logger.LogError(ex, "Error processing event. Offset will NOT be committed. Retrying in 2 seconds...");
                await Task.Delay(2000, stoppingToken);
            }
        }

        try
        {
            consumer.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while closing Kafka consumer.");
        }
    }
}