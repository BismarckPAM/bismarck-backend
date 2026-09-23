using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using AuthorizationService.Data;
using AuthorizationService.Models;
using Messaging;

namespace AuthorizationService.Services;

// Strong type representation for the approval-granted metadata payload
public record ApprovalGrantedMetadata(
    Guid? ApprovalId,
    Guid? UserId,
    Guid? ResourceId,
    int? RequestedLevel,
    int? DurationMinutes
);

public class ApprovalGrantedConsumer : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ApprovalGrantedConsumer> _logger;

    public ApprovalGrantedConsumer(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<ApprovalGrantedConsumer> logger)
    {
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield to allow the web application host to complete its startup sequence
        await Task.Yield();

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
            GroupId = _configuration["Kafka:GroupId"] ?? "bismarck-authorization-service",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();

        // Subscribe to the 'approval-granted' topic
        consumer.Subscribe(KafkaTopics.ApprovalGranted);
        _logger.LogInformation("ApprovalGrantedConsumer subscribed to '{Topic}' with GroupId '{GroupId}'", 
            KafkaTopics.ApprovalGranted, consumerConfig.GroupId);

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

                // Deserialize SecurityEvent<JsonElement>
                var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var secEvent = JsonSerializer.Deserialize<SecurityEvent<JsonElement>>(consumeResult.Message.Value, jsonOptions);

                if (secEvent is null || secEvent.EventId == Guid.Empty)
                {
                    _logger.LogWarning("Malformed event received on topic {Topic} at offset {Offset}. Retrying...",
                        consumeResult.Topic, consumeResult.Offset);
                    throw new FormatException($"Invalid payload at offset {consumeResult.Offset}");
                }

                // Read approval metadata
                var metadata = JsonSerializer.Deserialize<ApprovalGrantedMetadata>(
                    secEvent.Metadata.GetRawText(), jsonOptions);

                // Extract ApprovalId (fallback to EventId if not explicitly placed in metadata)
                var approvalId = metadata?.ApprovalId ?? secEvent.EventId;

                // Extract User & Resource IDs (with fallback to Actor and Resource envelope properties)
                var userId = metadata?.UserId 
                    ?? (Guid.TryParse(secEvent.Actor, out var parsedActor) ? parsedActor : Guid.Empty);

                var resourceId = metadata?.ResourceId 
                    ?? (Guid.TryParse(secEvent.Resource, out var parsedRes) ? parsedRes : Guid.Empty);

                int requestedLevel = metadata?.RequestedLevel ?? 1;
                int durationMinutes = metadata?.DurationMinutes ?? 60;

                // Calculate: ExpiresAt = OccurredAt + DurationMinutes
                var grantedAt = secEvent.OccurredAt;
                var expiresAt = grantedAt.AddMinutes(durationMinutes);

                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();

                // Check for duplicate ApprovalId (Idempotency)
                bool alreadyExists = await dbContext.TemporaryPermissions
                    .AnyAsync(p => p.ApprovalId == approvalId, stoppingToken);

                if (alreadyExists)
                {
                    _logger.LogWarning("ApprovalId {ApprovalId} already processed. Skipping duplicate insert and committing offset.", approvalId);
                    consumer.Commit(consumeResult);
                    continue;
                }

                // Insert ACTIVE temporary permission
                var permission = new TemporaryPermission
                {
                    Id = Guid.NewGuid(),
                    ApprovalId = approvalId,
                    UserId = userId,
                    ResourceId = resourceId,
                    RequestedLevel = requestedLevel,
                    GrantedAt = grantedAt,
                    ExpiresAt = expiresAt,
                    Status = TemporaryPermissionStatus.ACTIVE
                };

                dbContext.TemporaryPermissions.Add(permission);

                // Save to authorization_db
                try
                {
                    await dbContext.SaveChangesAsync(stoppingToken);
                }
                catch (DbUpdateException ex)
                {
                    // If Postgres unique constraint (code 23505) fires due to concurrent duplicate
                    if (ex.InnerException is PostgresException { SqlState: