using System.Text.Json;
using Confluent.Kafka;
using Messaging;

namespace AuthorizationService.Services;

/// <summary>
/// Publishes authorization audit events to Kafka.
///
/// The audit trail is a *side effect* of a policy decision: it must never turn
/// a completed decision into a failed request. This publisher therefore:
///   1. Fails fast (5s message / 3s socket timeouts) instead of hanging for the
///      120s librdkafka default when the broker is unreachable.
///   2. Never rethrows — Kafka failures are logged and swallowed.
///   3. Trips a lightweight circuit breaker after consecutive failures so
///      subsequent publishes skip the produce attempt entirely for a cool-down
///      window, keeping authorization checks fast while the bus is down.
/// </summary>
public sealed class KafkaAuthorizationEventPublisher : IAuthorizationEventPublisher, IDisposable
{
    private const int FailureThreshold = 3;
    private static readonly TimeSpan CircuitOpenDuration = TimeSpan.FromSeconds(15);

    private readonly IProducer<string, string> producer;
    private readonly ILogger<KafkaAuthorizationEventPublisher> logger;

    private readonly object circuitGate = new();
    private int consecutiveFailures;
    private DateTimeOffset circuitOpenedAt = DateTimeOffset.MinValue;

    public KafkaAuthorizationEventPublisher(
        IConfiguration configuration,
        ILogger<KafkaAuthorizationEventPublisher> logger)
    {
        this.logger = logger;
        producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
            Acks = Acks.All,
            EnableIdempotence = true,
            // Fail fast rather than block a request thread for librdkafka's default.
            MessageTimeoutMs = 5000,
            SocketTimeoutMs = 3000,
            RequestTimeoutMs = 3000
        }).Build();
    }

    public async Task PublishAsync<T>(
        string topic,
        SecurityEvent<T> message,
        CancellationToken cancellationToken = default)
    {
        if (IsCircuitOpen())
        {
            logger.LogWarning(
                "Authorization event {EventId} to {Topic} skipped: Kafka circuit breaker is open.",
                message.EventId,
                topic);
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var result = await producer.ProduceAsync(topic, new Message<string, string>
            {
                Key = message.EventId.ToString(),
                Value = json
            }, cancellationToken);

            OnSuccess();
            logger.LogInformation(
                "Published authorization event {EventId} to {Topic} at partition {Partition}.",
                message.EventId,
                topic,
                result.Partition.Value);
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled — do not trip the breaker for an intentional abort.
            throw;
        }
        catch (Exception exception)
        {
            OnFailure(exception, message.EventId, topic);
        }
    }

    private bool IsCircuitOpen()
    {
        lock (circuitGate)
        {
            if (consecutiveFailures < FailureThreshold)
            {
                return false;
            }

            if (DateTimeOffset.UtcNow - circuitOpenedAt >= CircuitOpenDuration)
            {
                // Cool-down elapsed: half-open — allow the next attempt through.
                consecutiveFailures = 0;
                return false;
            }

            return true;
        }
    }

    private void OnSuccess()
    {
        lock (circuitGate)
        {
            consecutiveFailures = 0;
        }
    }

    private void OnFailure(Exception exception, Guid eventId, string topic)
    {
        lock (circuitGate)
        {
            consecutiveFailures++;
            if (consecutiveFailures >= FailureThreshold)
            {
                circuitOpenedAt = DateTimeOffset.UtcNow;
            }
        }

        logger.LogError(
            exception,
            "Failed to publish authorization event {EventId} to {Topic}. " +
            "The audit trail will be incomplete but the policy decision is unaffected.",
            eventId,
            topic);
    }

    public void Dispose()
    {
        try
        {
            producer.Flush(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Error while flushing the Kafka producer.");
        }

        producer.Dispose();
    }
}