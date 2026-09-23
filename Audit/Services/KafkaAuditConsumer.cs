using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
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
                // 1. Consume event (blocking call with stoppingToken)
                var consumeResult = consumer.Consume(stoppingToken);
                if (consumeResult?.Message?.Value is null)
                {
                    continue;
                }

                // 2. Deserialize event using generic SecurityEvent with JsonElement for dynamic metadata
                var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var secEvent = JsonSerializer.Deserialize<SecurityEvent<JsonElement>>(consumeResult.Message.Value, jsonOptions);

                if (secEvent is null)
                {
                    _logger.LogWarning("Failed to deserialize event at topic {Topic}, partition {Partition}, offset {Offset}",
                        consumeResult.Topic, consumeResult.Partition, consumeResult.Offset);
                    consumer.Commit(consumeResult);
                    continue;
                }

                // Create a dedicated DI scope for the scoped DbContext
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

                // Check if EventId already exists in database 
                bool alreadyExists = await dbContext.AuditLogs
                    .AnyAsync(a => a.EventId == secEvent.EventId, stoppingToken);

                if (alreadyExists)
                {
                    _logger.LogWarning("Duplicate event detected (EventId: {EventId}). Skipping insert and committing offset.", secEvent.EventId);
                    
                    // EventId already exists -> do not insert again -> commit the offset
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
                    // Concurrency safeguard: handles potential race conditions on unique index
                    _logger.LogWarning(ex, "Unique constraint hit for EventId: {EventId}. Skipping duplicate.", secEvent.EventId);
                }

                // 5. Commit Kafka offset (strictly AFTER database save completes)
                consumer.Commit(consumeResult);

                _logger.LogInformation("Successfully processed and committed audit event {EventId} from topic {Topic}", 
                    secEvent.EventId, consumeResult.Topic);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("KafkaAuditConsumer cancellation requested. Stopping consumer loop.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while processing message. Offset will NOT be committed.");
                // Delay briefly before retry to prevent busy-looping if database or network is down
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