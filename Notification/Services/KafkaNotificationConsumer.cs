using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Messaging;
using Notification.Service.Data;
using Notification.Service.Models;

namespace Notification.Service.Services;

public class KafkaNotificationConsumer : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KafkaNotificationConsumer> _logger;

    public KafkaNotificationConsumer(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<KafkaNotificationConsumer> logger)
    {
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var bootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var deadLetterTopic = _configuration["Kafka:DeadLetterTopic"] ?? "notification-dead-letter";

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = _configuration["Kafka:GroupId"] ?? "bismarck-notification-service",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All // Ensure DLQ messages are safely acknowledged
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();
        using var deadLetterProducer = new ProducerBuilder<Null, string>(producerConfig).Build();

        // identity-events are per-user (the affected user is the actor) so they
        // generate user notifications. resource-events are intentionally NOT
        // subscribed: their actor is a team name, not a user, so there is no
        // per-user recipient and they would only fill the dead-letter topic.
        // Audit (which records history rather than per-user messages) does
        // subscribe to resource-events.
        var topics = new[]
        {
            KafkaTopics.AccessRequested,
            KafkaTopics.AccessGranted,
            KafkaTopics.AccessDenied,
            KafkaTopics.ApprovalRequested,
            KafkaTopics.ApprovalGranted,
            KafkaTopics.ApprovalRejected,
            KafkaTopics.PermissionRevoked,
            KafkaTopics.IdentityEvents
        };

        consumer.Subscribe(topics);
        _logger.LogInformation("KafkaNotificationConsumer started with GroupId '{GroupId}'. Subscribed to: {Topics}", 
            consumerConfig.GroupId, string.Join(", ", topics));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
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

                // Forward malformed payloads to DLQ and commit offset
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

                    consumer.Commit(consumeResult);
                    continue;
                }

                // Resolve target recipient and validate Guid
                var targetUserStr = ResolveTargetUser(secEvent!, consumeResult.Topic);
                if (string.IsNullOrWhiteSpace(targetUserStr) || !Guid.TryParse(targetUserStr, out var targetUserId))
                {
                    var reason = string.IsNullOrWhiteSpace(targetUserStr)
                        ? "Unable to determine target recipient UserId from event."
                        : $"Resolved user '{targetUserStr}' is not a valid Guid.";

                    _logger.LogWarning("{Reason} for EventId {EventId} (Topic: {Topic}). Routing to DLQ.", 
                        reason, secEvent!.EventId, consumeResult.Topic);

                    var dltHeaders = new Headers
                    {
                        { "x-original-topic", Encoding.UTF8.GetBytes(consumeResult.Topic) },
                        { "x-original-offset", Encoding.UTF8.GetBytes(consumeResult.Offset.Value.ToString()) },
                        { "x-exception-message", Encoding.UTF8.GetBytes(reason) }
                    };

                    await deadLetterProducer.ProduceAsync(deadLetterTopic, new Message<Null, string>
                    {
                        Value = consumeResult.Message.Value,
                        Headers = dltHeaders
                    }, stoppingToken);

                    consumer.Commit(consumeResult);
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

                // Idempotency Check: prevent duplicate notifications
                bool alreadyExists = await dbContext.Notifications
                    .AnyAsync(n => n.EventId == secEvent!.EventId, stoppingToken);

                if (alreadyExists)
                {
                    _logger.LogWarning("Duplicate event detected (EventId: {EventId}). Skipping insert and committing offset.", secEvent!.EventId);
                    consumer.Commit(consumeResult);
                    continue;
                }

                // Generate Title and Message
                var (title, message) = GenerateContent(secEvent!, consumeResult.Topic);

                var notification = new NotificationLog
                {
                    Id = Guid.NewGuid(),
                    EventId = secEvent!.EventId,
                    UserId = targetUserId, // Guid assigned here
                    EventType = secEvent.EventType,
                    Title = title,
                    Message = message,
                    ResourceId = secEvent.Resource,
                    CreatedAt = secEvent.OccurredAt,
                    IsRead = false
                };

                dbContext.Notifications.Add(notification);

                // Save with Outage Handling
                try
                {
                    await dbContext.SaveChangesAsync(stoppingToken);
                }
                catch (DbUpdateException ex)
                {
                    if (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                    {
                        _logger.LogWarning("EventId {EventId} was saved by another consumer instance. Skipping duplicate.", secEvent.EventId);
                    }
                    else
                    {
                        // Throw to leave Kafka offset uncommitted so it retries on database recovery
                        _logger.LogError(ex, "Database save failed for EventId {EventId}. Leaving offset uncommitted.", secEvent.EventId);
                        throw;
                    }
                }

                // Commit Kafka offset ONLY after DB save succeeds
                consumer.Commit(consumeResult);

                _logger.LogInformation("Successfully created notification for User {UserId} from EventId {EventId} (Topic: {Topic})", 
                    targetUserId, secEvent.EventId, consumeResult.Topic);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("KafkaNotificationConsumer cancellation requested. Stopping loop.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing notification event. Offset will NOT be committed. Retrying in 2 seconds...");
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


    // Explicitly determines the target recipient based on event topic and metadata rules.
    
    private static string? ResolveTargetUser(SecurityEvent<JsonElement> secEvent, string topic)
    {
        var metadataUserId = TryGetMetadataProperty(secEvent.Metadata, "UserId", "userId");
        var requesterUserId = TryGetMetadataProperty(secEvent.Metadata, "RequesterUserId", "requesterUserId", "RequesterId", "requesterId");

        return topic switch
        {
            // Approval events: target the requester who is waiting for outcome
            KafkaTopics.ApprovalGranted   => metadataUserId ?? requesterUserId ?? secEvent.Actor,
            KafkaTopics.ApprovalRejected  => metadataUserId ?? requesterUserId ?? secEvent.Actor,
            KafkaTopics.ApprovalRequested => requesterUserId ?? metadataUserId ?? secEvent.Actor,

            // Access events: target actor or explicit user in metadata
            KafkaTopics.AccessRequested   => !string.IsNullOrWhiteSpace(secEvent.Actor) ? secEvent.Actor : metadataUserId,
            KafkaTopics.AccessGranted     => !string.IsNullOrWhiteSpace(secEvent.Actor) ? secEvent.Actor : metadataUserId,
            KafkaTopics.AccessDenied      => !string.IsNullOrWhiteSpace(secEvent.Actor) ? secEvent.Actor : metadataUserId,

            // Permission revoked: explicitly targets the affected user
            KafkaTopics.PermissionRevoked => metadataUserId ?? secEvent.Actor,

            // Identity events: the affected user is the actor (their GUID)
            KafkaTopics.IdentityEvents    => metadataUserId ?? secEvent.Actor,

            _ => metadataUserId ?? secEvent.Actor
        };
    }


    // Generates friendly titles and messages based on event type and resource.

    private static (string Title, string Message) GenerateContent(SecurityEvent<JsonElement> secEvent, string topic)
    {
        var resource = string.IsNullOrWhiteSpace(secEvent.Resource) ? "the requested resource" : secEvent.Resource;

        return topic switch
        {
            KafkaTopics.AccessRequested => (
                "Access Requested",
                $"Access request submitted for {resource}."
            ),
            KafkaTopics.AccessGranted => (
                "Access Granted",
                $"You have been granted access to {resource}."
            ),
            KafkaTopics.AccessDenied => (
                "Access Denied",
                $"Your access request to {resource} was denied."
            ),
            KafkaTopics.ApprovalRequested => (
                "Approval Requested",
                $"Your approval request for {resource} has been submitted for review."
            ),
            KafkaTopics.ApprovalGranted => (
                "Approval Granted",
                $"Your request for {resource} has been approved."
            ),
            KafkaTopics.ApprovalRejected => (
                "Approval Rejected",
                $"Your request for {resource} was rejected."
            ),
            KafkaTopics.PermissionRevoked => (
                "Permission Revoked",
                $"Your permissions for {resource} have been revoked."
            ),
            KafkaTopics.IdentityEvents => secEvent.EventType switch
            {
                "user-created" => (
                    "Account Created",
                    "Your account has been created."
                ),
                "user-updated" => (
                    "Account Updated",
                    "Your account details have been updated."
                ),
                _ => (
                    "Account Update",
                    $"An account event '{secEvent.EventType}' occurred."
                )
            },
            _ => (
                secEvent.EventType,
                $"Event {secEvent.Action} occurred on {resource} with outcome '{secEvent.Outcome}'."
            )
        };
    }

    private static string? TryGetMetadataProperty(JsonElement metadata, params string[] propertyNames)
    {
        if (metadata.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in propertyNames)
        {
            if (metadata.TryGetProperty(name, out var element))
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var val = element.GetString();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        return val;
                    }
                }
            }
        }

        return null;
    }
}