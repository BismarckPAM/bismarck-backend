using System.Security.Claims;
using AuthorizationService.Data;
using AuthorizationService.DTOs;
using AuthorizationService.Models;
using AuthorizationService.Services;
using Messaging;
using Microsoft.AspNetCore.Authorization;
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
[Authorize]
[Route("api/jit/sessions")]
public sealed class JitSessionsController(
    AuthorizationDbContext dbContext,
    IAzureJitProvisioner provisioner,
    IJitTerminalBroker terminalBroker,
    IAuthorizationEventPublisher eventPublisher,
    ISystemClock clock) : ControllerBase
{
    [NonAction]
    public Task<ActionResult<IReadOnlyList<JitSessionResponse>>> GetSessions(
        bool activeOnly,
        CancellationToken cancellationToken)
    {
        return GetSessions(activeOnly, null, null, cancellationToken);
    }

    [NonAction]
    public Task<IActionResult> Revoke(
        Guid id,
        RevokeJitSessionRequest? request,
        CancellationToken cancellationToken)
    {
        return Revoke(id, request, null, null, cancellationToken);
    }

    /// <summary>
    /// Roles allowed to enumerate every JIT session and to revoke any of them.
    /// A Security Admin is deliberately NOT an approver — that authority stays
    /// with the Approval Service's configured approver roles.
    /// </summary>
    private static readonly string[] PrivilegedRoles = ["Admin", "Security Admin"];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<JitSessionResponse>>> GetSessions(
        [FromQuery] bool activeOnly,
        [FromHeader(Name = "X-User-Role")] string? userRole,
        [FromHeader(Name = "X-User-Id")] string? headerUserId,
        CancellationToken cancellationToken)
    {
        var (callerId, isAdmin) = ResolveCaller(userRole, headerUserId);

        var query = dbContext.TemporaryPermissions.AsNoTracking().AsQueryable();
        if (!isAdmin)
        {
            if (callerId is null)
                return Forbid();

            query = query.Where(p => p.UserId == callerId.Value);
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
        [FromHeader(Name = "X-User-Role")] string? userRole,
        [FromHeader(Name = "X-User-Id")] string? headerUserId,
        CancellationToken cancellationToken)
    {
        var (adminId, isAdmin) = ResolveCaller(userRole, headerUserId);

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

        // Drop any live brokered terminal immediately, so a manual revoke actually
        // ends the session rather than only updating the record.
        await terminalBroker.CloseAsync(permission.Id, "JIT session revoked by administrator.");

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
                    : request.Reason
            });

        await eventPublisher.PublishAsync(KafkaTopics.JitRevoked, revokedEvent, cancellationToken);

        return Ok(ToResponse(permission, now));
    }

    private (Guid? UserId, bool IsAdmin) ResolveCaller(string? userRole, string? userId)
    {
        var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value
            ?? userRole;

        // Admin OR Security Admin may see every session and manually revoke any of
        // them. Exact allow-list on normalized values — never a substring test like
        // role.Contains("admin"), which would wrongly privilege unrelated roles.
        var isAdmin = PrivilegedRoles.Any(privileged => User.IsInRole(privileged))
            || (roleClaim is not null
                && PrivilegedRoles.Any(privileged =>
                    string.Equals(privileged.Trim(), roleClaim.Trim(), StringComparison.OrdinalIgnoreCase)));

        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value
            ?? userId;

        Guid? callerId = Guid.TryParse(idClaim, out var parsed) ? parsed : null;
        return (callerId, isAdmin);
    }

    private static JitSessionResponse ToResponse(TemporaryPermission permission, DateTimeOffset now)
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
            permission.ProvisioningDetail,
            permission.TargetVmName,
            permission.TargetResourceGroup,
            permission.TargetHost,
            permission.TargetOsType,
            permission.ConnectionCommand);
    }
}