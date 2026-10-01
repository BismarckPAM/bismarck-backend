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
    private readonly IAuthorizationEventPublisher _eventPublisher;
    private readonly IAzureJitProvisioner _provisioner;

    // Check every 30 seconds 
    private readonly TimeSpan _checkInterval;

    public TemporaryPermissionExpirationWorker(
        IServiceScopeFactory scopeFactory,
        ISystemClock clock,
        IConfiguration configuration,
        ILogger<TemporaryPermissionExpirationWorker> logger,
        IAuthorizationEventPublisher eventPublisher,
        IAzureJitProvisioner provisioner)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _configuration = configuration;
        _logger = logger;
        _eventPublisher = eventPublisher;
        _provisioner = provisioner;

        var intervalSeconds = configuration.GetValue("ExpirationWorker:IntervalSeconds", 30);
        _checkInterval = TimeSpan.FromSeconds(intervalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        using var timer = new PeriodicTimer(_checkInterval);

        _logger.LogInformation("TemporaryPermissionExpirationWorker started. Interval: {Interval}s", _checkInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ExpirePermissionsOnceAsync(stoppingToken);
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

        _logger.LogInformation("TemporaryPermissionExpirationWorker stopped.");
    }

    public async Task ExpirePermissionsOnceAsync(CancellationToken ct = default)
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

        // 1. Remove the cloud-side grant (Azure role assignment) FIRST —
        //    best effort, never throws, so the local state machine always
        //    advances even when the cloud API is unavailable.
        foreach (var permission in expiredPermissions)
        {
            try
            {
                var cloudResult = await _provisioner.RevokeAsync(permission, ct);
                // Only overwrite the detail when revocation actually succeeded.
                // Otherwise the revoke failure would clobber the reason the GRANT
                // failed, destroying the diagnostic for the original problem.
                if (cloudResult.Succeeded)
                {
                    permission.ProvisioningStatus = "EXPIRED";
                    permission.ProvisioningDetail = cloudResult.Detail;
                }
                else
                {
                    _logger.LogWarning(
                        "Cloud revocation for PermissionId {PermissionId} reported: {Detail}",
                        permission.Id, cloudResult.Detail);
                }
            }
            catch (Exception cloudException) when (cloudException is not OperationCanceledException)
            {
                _logger.LogError(cloudException,
                    "Cloud revocation failed for PermissionId {PermissionId}; marking EXPIRED locally anyway.",
                    permission.Id);
            }

            // 2. Advance the local status to EXPIRED.
            permission.Status = TemporaryPermissionStatus.EXPIRED;
            permission.RevokedAt = now;
        }

        // Save to database FIRST before publishing events
        await dbContext.SaveChangesAsync(ct);

        // Publish PermissionRevoked event for each expired record
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

            try
            {
                await _eventPublisher.PublishAsync(
                    KafkaTopics.PermissionRevoked,
                    revokedEvent,
                    ct);

                _logger.LogInformation("Published PermissionRevoked event for PermissionId {PermissionId} (UserId {UserId})", 
                    permission.Id, permission.UserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish PermissionRevoked event for PermissionId {PermissionId}", permission.Id);
            }
        }
    }
}