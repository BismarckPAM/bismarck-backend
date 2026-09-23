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

        var config = new ConsumerConfig
        {
            BootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
            GroupId = "bismarck-audit-service",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();

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
        _logger.LogInformation("KafkaAuditConsumer started. Subscribed to topics: {Topics}", string.Join(", ", topics));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. Consume event
                var consumeResult = consumer.Consume(stoppingToken);
                if (consumeResult?.Message?.Value is null)
                {
                    continue;
                }

                // 2. Deserialize event
                SecurityEvent<JsonElement>? secEvent = null;
                try
                {
                    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    secEvent = JsonSerializer.Deserialize<SecurityEvent<JsonElement>>(consumeResult.Message.Value, jsonOptions);
                }
                catch (JsonException ex)
                {
                    // Do NOT commit on malformed JSON. Throw to trigger retry / alert.
                    throw new FormatException($"Malformed JSON message at topic {consumeResult.Topic}, offset {consumeResult.Offset}", ex);
                }

                if (secEvent is null || secEvent.EventId == Guid.Empty)
                {
                    // Do NOT commit invalid payload.
                    throw new FormatException($"Deserialized event is null or has empty EventId at topic {consumeResult.Topic}, offset {consumeResult.Offset}");
                }

                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

                // Check if EventId already exists in database 
                bool alreadyExists = await dbContext.AuditLogs
                    .AnyAsync(a => a.EventId == secEvent.EventId, stoppingToken);

                if (alreadyExists)
                {
                    _logger.LogWarning("Duplicate event detected (EventId: {EventId}). Skipping insert and committing offset.", secEvent.EventId);
                    consumer.Commit(consumeResult);
                    continue;
                }

                // 3. Insert AuditLog
                var auditLog = new AuditLog
                {
                    Id = Guid.NewGuid(),
                    EventId = secEvent.EventId,
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

                // 4. SaveChangesAsync
                try
                {
                    await dbContext.SaveChangesAsync(stoppingToken);
                }
                catch (DbUpdateException ex)
                {
                    // Only ignore if it is a CONFIRMED duplicate EventId 
                    bool isConfirmedDuplicate = ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
                        || await dbContext.AuditLogs.AsNoTracking().AnyAsync(a => a.EventId == secEvent.EventId, stoppingToken);

                    if (isConfirmedDuplicate)
                    {
                        _logger.LogWarning("Confirmed duplicate EventId {EventId} during insert. Proceeding to commit.", secEvent.EventId);
                    }
                    else
                    {
                        // Any other database outage/error MUST throw and retry
                        _logger.LogError(ex, "Database update failed with a non-duplicate error for EventId {EventId}. Retrying without committing.", secEvent.EventId);
                        throw; 
                    }
                }

                // 5. Commit Kafka offset 
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
                // Unhandled DB outage or deserialization failure reaches here:
                // Offset is NEVER committed -> message will be retried
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