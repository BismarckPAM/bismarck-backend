using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using AuthorizationService.Data;
using AuthorizationService.Models;
using Messaging;

namespace AuthorizationService.Services;

public class TemporaryPermissionExpirationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISystemClock _clock;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TemporaryPermissionExpirationWorker> _logger;

    // Check every 30 seconds 
    private readonly TimeSpan _checkInterval;

    public TemporaryPermissionExpirationWorker(
        IServiceScopeFactory scopeFactory,
        ISystemClock clock,
        IConfiguration configuration,
        ILogger<TemporaryPermissionExpirationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _configuration = configuration;
        _logger = logger;

        var intervalSeconds = configuration.GetValue("ExpirationWorker:IntervalSeconds", 30);
        _checkInterval = TimeSpan.FromSeconds(intervalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
            Acks = Acks.All
        };

        using var producer = new ProducerBuilder<string, string>(producerConfig).Build();
        using var timer = new PeriodicTimer(_checkInterval);

        _logger.LogInformation("TemporaryPermissionExpirationWorker started. Interval: {Interval}s", _checkInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ExpirePermissionsAsync(producer, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred during permission expiration sweep.");
            }
        }

        producer.Flush(TimeSpan.FromSeconds(5));
        _logger.LogInformation("TemporaryPermissionExpirationWorker stopped.");
    }

    private async Task ExpirePermissionsAsync(IProducer<string, string> producer, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();

        var now = _clock.UtcNow;

        // 1. Query only records that are ACTIVE and expired (ignores already EXPIRED/REVOKED)
        var expiredPermissions = await dbContext.TemporaryPermissions
            .Where(p => p.Status == TemporaryPermissionStatus.ACTIVE && p.ExpiresAt <= now)
            .OrderBy(p => p.ExpiresAt)
            .Take(100) 
            .ToListAsync(ct);

        if (expiredPermissions.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Found {Count} expired active temporary permissions to process at {Now}", 
            expiredPermissions.Count, now);

        // Update status and revocation timestamp
        foreach (var permission in expiredPermissions)
        {
            permission.Status = TemporaryPermissionStatus.EXPIRED;
            permission.RevokedAt = now;
        }

        // Save to database FIRST before publishing events
        await dbContext.SaveChangesAsync(ct);

        // Publish PermissionRevoked event for each expired record
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        foreach (var permission in expiredPermissions)
        {
            var revokedEvent = new SecurityEvent<object>(
                EventId: Guid.NewGuid(),
                EventType: SecurityEventTypes.PermissionRevoked,
                OccurredAt: now,
                Actor: "system:expiration-worker",
                Resource: permission.ResourceId.ToString(),
                Action: "EXPIRE_PERMISSION",
                Outcome: "SUCCESS",
                Metadata: new
                {
                    PermissionId = permission.Id,
                    ApprovalId = permission.ApprovalId,
                    UserId = permission.UserId,
                    ResourceId = permission.ResourceId,
                    RequestedLevel = permission.RequestedLevel,
                    GrantedAt = permission.GrantedAt,
                    ExpiresAt = permission.ExpiresAt,
                    RevokedAt = permission.RevokedAt,
                    Reason = "Automatic expiration by background worker past TTL."
                }
            );

            var json = JsonSerializer.Serialize(revokedEvent, jsonOptions);

            try
            {
                await producer.ProduceAsync(KafkaTopics.PermissionRevoked, new Message<string, string>
                {
                    Key = permission.UserId.ToString(), 
                    Value = json
                }, ct);

                _logger.LogInformation("Published PermissionRevoked event for PermissionId {PermissionId} (UserId {UserId})", 
                    permission.Id, permission.UserId);
            }
            catch (ProduceException<string, string> ex)
            {
                _logger.LogError(ex, "Failed to publish PermissionRevoked event for PermissionId {PermissionId}", permission.Id);
            }
        }
    }
}