using System.Security.Claims;
using AuthorizationService.Data;
using AuthorizationService.DTOs;
using AuthorizationService.Models;
using AuthorizationService.Services;
using Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuthorizationService.Controllers;

/// <summary>
///  GET  /api/jit/sessions             — active + recent JIT sessions. Regular
///                                       users see their own; Admins see all.
///  POST /api/jit/sessions/{id}/revoke — Admin-only immediate revocation:
///                                       removes the cloud role assignment,
///                                       marks the session REVOKED and emits a
///                                       jit-revoked event for audit.
/// </summary>
[ApiController]
[Route("api/jit/sessions")]
public sealed class JitSessionsController(
    AuthorizationDbContext dbContext,
    IAzureJitProvisioner provisioner,
    IAuthorizationEventPublisher eventPublisher,
    ISystemClock clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<JitSessionResponse>>> GetSessions(
        [FromQuery] bool activeOnly,
        CancellationToken cancellationToken)
    {
        var (userId, isAdmin) = ResolveCaller();

        var query = dbContext.TemporaryPermissions.AsNoTracking().AsQueryable();
        if (!isAdmin)
        {
            if (userId is null)
                return Forbid();

            query = query.Where(p => p.UserId == userId.Value);
        }

        if (activeOnly)
            query = query.Where(p => p.Status == TemporaryPermissionStatus.ACTIVE);

        var now = clock.UtcNow;
        var permissions = await query
            .OrderByDescending(p => p.GrantedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        return Ok(permissions.Select(p => ToResponse(p, now)).ToList());
    }

    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(
        [FromRoute] Guid id,
        [FromBody] RevokeJitSessionRequest? request,
        CancellationToken cancellationToken)
    {
        var (adminId, isAdmin) = ResolveCaller();

        if (!isAdmin)
            return Forbid();

        var permission = await dbContext.TemporaryPermissions
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (permission is null)
            return NotFound(new { message = $"JIT session '{id}' was not found." });

        var now = clock.UtcNow;
        if (permission.Status != TemporaryPermissionStatus.ACTIVE || permission.ExpiresAt <= now)
        {
            return Conflict(new
            {
                message = $"Cannot revoke the session. It is already {permission.Status} or has expired.",
                permission.Status,
                permission.ExpiresAt
            });
        }

        // Remove the cloud grant FIRST (best effort, never throws).
        try
        {
            var cloudResult = await provisioner.RevokeAsync(permission, cancellationToken);
            permission.ProvisioningStatus = cloudResult.Succeeded ? "REVOKED" : permission.ProvisioningStatus;
            permission.ProvisioningDetail = cloudResult.Detail ?? permission.ProvisioningDetail;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            permission.ProvisioningDetail = "Cloud revocation failed during manual revoke.";
            // Never let a cloud failure block the local state transition.
            _ = exception;
        }

        permission.Status = TemporaryPermissionStatus.REVOKED;
        permission.RevokedAt = now;
        permission.RevokedByUserId = adminId;

        await dbContext.SaveChangesAsync(cancellationToken);

        var revokedEvent = new SecurityEvent<object>(
            EventId: Guid.NewGuid(),
            EventType: SecurityEventTypes.PermissionRevoked,
            OccurredAt: now,
            Actor: adminId?.ToString() ?? "system:admin",
            Resource: permission.ResourceId.ToString(),
            Action: "MANUAL_REVOKE_JIT_SESSION",
            Outcome: "SUCCESS",
            Metadata: new
            {
                PermissionId = permission.Id,
                permission.ApprovalId,
                permission.UserId,
                permission.UserEmail,
                permission.ResourceId,
                permission.ResourceName,
                permission.RequestedLevel,
                RevokedByUserId = adminId,
                RevokedAt = now,
                Reason = string.IsNullOrWhiteSpace(request?.Reason)
                    ? "Manual revocation by administrator"
                    : request!.Reason
            });

        await eventPublisher.PublishAsync(KafkaTopics.JitRevoked, revokedEvent, cancellationToken);

        return Ok(ToResponse(permission, now));
    }

    private (Guid? UserId, bool IsAdmin) ResolveCaller()
    {
        var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value
            ?? Request.Headers["X-User-Role"].FirstOrDefault();

        var isAdmin = User.IsInRole("Admin")
            || string.Equals(roleClaim, "Admin", StringComparison.OrdinalIgnoreCase);

        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value
            ?? Request.Headers["X-User-Id"].FirstOrDefault();

        Guid? userId = Guid.TryParse(idClaim, out var parsed) ? parsed : null;
        return (userId, isAdmin);
    }

    private JitSessionResponse ToResponse(TemporaryPermission permission, DateTimeOffset now)
    {
        var remaining = permission.Status == TemporaryPermissionStatus.ACTIVE
            ? (int)Math.Max(0, (permission.ExpiresAt - now).TotalSeconds)
            : 0;

        return new JitSessionResponse(
            permission.Id,
            permission.ApprovalId,
            permission.UserId,
            permission.UserEmail,
            permission.ResourceId,
            permission.ResourceName,
            permission.Action,
            permission.RequestedLevel,
            permission.Status.ToString(),
            permission.GrantedAt,
            permission.ExpiresAt,
            permission.RevokedAt,
            permission.RevokedByUserId,
            remaining,
            permission.ProvisioningStatus,
            permission.ProvisioningDetail);
    }
}