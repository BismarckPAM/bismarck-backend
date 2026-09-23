using System.Security.Claims;
using AuthorizationService.Clients;
using AuthorizationService.Data;
using AuthorizationService.DTOs;
using AuthorizationService.Models;
using AuthorizationService.Services;
using Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuthorizationService.Controllers;

[ApiController]
[Route("api/authorization")]
public sealed class AuthorizationController(
    IIdentityServiceClient identityServiceClient,
    IResourceServiceClient resourceServiceClient,
    IPolicyDecisionEngine policyDecisionEngine,
    IAuthorizationEventPublisher eventPublisher,
    AuthorizationDbContext dbContext,
    ISystemClock clock) : ControllerBase
{

    //MANUAL REVOKE 

    [HttpPost("permissions/{id:guid}/revoke")]
    public async Task<IActionResult> RevokePermission(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value
            ?? Request.Headers["X-User-Role"].FirstOrDefault();

        bool isAdmin = User.IsInRole("Admin") 
            || string.Equals(roleClaim, "Admin", StringComparison.OrdinalIgnoreCase);

        if (!isAdmin)
        {
            return Forbid(); 
        }

        // Extract admin user ID
        var adminIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value
            ?? Request.Headers["X-User-Id"].FirstOrDefault();

        Guid? adminId = Guid.TryParse(adminIdClaim, out var parsedAdminId) ? parsedAdminId : null;

        var permission = await dbContext.TemporaryPermissions
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (permission is null)
        {
            return NotFound(new { message = $"Temporary permission with ID '{id}' was not found." });
        }

        var now = clock.UtcNow;

        // Already expired/revoked -> 409
        if (permission.Status != TemporaryPermissionStatus.ACTIVE || permission.ExpiresAt <= now)
        {
            return Conflict(new 
            { 
                message = $"Cannot revoke permission. It is already marked as {permission.Status} or has expired.",
                permission.Status,
                permission.ExpiresAt
            });
        }

        // Update permission
        permission.Status = TemporaryPermissionStatus.REVOKED;
        permission.RevokedAt = now;
        permission.RevokedByUserId = adminId;

        await dbContext.SaveChangesAsync(cancellationToken);

        // Publish PermissionRevoked event
        var revokedEvent = new SecurityEvent<object>(
            EventId: Guid.NewGuid(),
            EventType: SecurityEventTypes.PermissionRevoked,
            OccurredAt: now,
            Actor: adminId?.ToString() ?? "system:admin",
            Resource: permission.ResourceId.ToString(),
            Action: "MANUAL_REVOKE_PERMISSION",
            Outcome: "SUCCESS",
            Metadata: new
            {
                PermissionId = permission.Id,
                permission.ApprovalId,
                permission.UserId,
                permission.ResourceId,
                permission.RequestedLevel,
                RevokedByUserId = adminId,
                RevokedAt = now,
                Reason = "Manual revocation by administrator"
            });

        await eventPublisher.PublishAsync(KafkaTopics.PermissionRevoked, revokedEvent, cancellationToken);

        // Rule: Admin + ACTIVE -> 200
        return Ok(new
        {
            message = "Temporary permission revoked successfully.",
            permission.Id,
            Status = permission.Status.ToString(),
            permission.RevokedAt
        });
    }

    [HttpPost("check")]
    public async Task<ActionResult<AuthorizationDecisionResult>> Check(
        AuthorizationCheckRequest request,
        CancellationToken cancellationToken)
    {
        var userTask = identityServiceClient.GetUserRoleAsync(
            request.UserId,
            cancellationToken: cancellationToken);
        var resourceTask = resourceServiceClient.GetResourceContextAsync(
            request.ResourceId,
            cancellationToken: cancellationToken);

        await Task.WhenAll(userTask, resourceTask);

        var user = await userTask;
        if (user.Value is null)
        {
            var reason = user.FailureReason ?? AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED;
            var decision = AuthorizationDecisionResult.Deny(reason);
            await PublishEventAsync(request, decision, cancellationToken);
            return Ok(decision);
        }

        if (!user.Value.IsActive)
        {
            var decision = AuthorizationDecisionResult.Deny(AuthorizationDenialReason.USER_DEACTIVATED);
            await PublishEventAsync(request, decision, cancellationToken);
            return Ok(decision);
        }

        if (string.IsNullOrWhiteSpace(user.Value.RoleName))
        {
            var decision = AuthorizationDecisionResult.Deny(AuthorizationDenialReason.USER_ROLE_NOT_ASSIGNED);
            await PublishEventAsync(request, decision, cancellationToken);
            return Ok(decision);
        }

        var resource = await resourceTask;
        if (resource.Value is null)
        {
            var reason = resource.FailureReason ?? AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED;
            var decision = AuthorizationDecisionResult.Deny(reason);
            await PublishEventAsync(request, decision, cancellationToken);
            return Ok(decision);
        }

        var result = await policyDecisionEngine.EvaluateAsync(
            new UserDto(user.Value.Id, user.Value.RoleName, user.Value.IsActive),
            new ResourceDto(resource.Value.Id, resource.Value.Type, resource.Value.Environment, resource.Value.Criticality),
            request.Action,
            cancellationToken);

        if (result.Decision == AuthorizationDecision.ALLOW)
        {
            result = result with
            {
                ExpiresAt = clock.UtcNow.AddMinutes(request.SessionDurationMinutes)
            };
        }

        await PublishEventAsync(request, result, cancellationToken);
        return Ok(result);
    }

    private async Task PublishEventAsync(
        AuthorizationCheckRequest request,
        AuthorizationDecisionResult decision,
        CancellationToken cancellationToken)
    {
        var (eventType, topic, outcome) = decision.Decision switch
        {
            AuthorizationDecision.ALLOW =>
                ("AccessGranted", KafkaTopics.AccessGranted, "ALLOWED"),
            AuthorizationDecision.APPROVAL_REQUIRED =>
                ("AccessRequested", KafkaTopics.AccessRequested, "APPROVAL_REQUIRED"),
            _ =>
                ("AccessDenied", KafkaTopics.AccessDenied, "DENIED")
        };

        await eventPublisher.PublishAsync(
            topic,
            new SecurityEvent<object>(
                Guid.NewGuid(),
                eventType,
                DateTimeOffset.UtcNow,
                request.UserId.ToString(),
                request.ResourceId.ToString(),
                request.Action,
                outcome,
                new
                {
                    decision.Reason,
                    decision.Details,
                    decision.ExpiresAt,
                    decision.ApprovalRequirement
                }),
            cancellationToken);
    }
}