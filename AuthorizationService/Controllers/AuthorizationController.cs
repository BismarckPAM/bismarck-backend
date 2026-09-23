using AuthorizationService.Clients;
using AuthorizationService.DTOs;
using AuthorizationService.Models;
using AuthorizationService.Services;
using Messaging;
using Microsoft.AspNetCore.Mvc;

namespace AuthorizationService.Controllers;

[ApiController]
[Route("api/authorization")]
public sealed class AuthorizationController(
    IIdentityServiceClient identityServiceClient,
    IResourceServiceClient resourceServiceClient,
    IPolicyDecisionEngine policyDecisionEngine,
    IAuthorizationEventPublisher eventPublisher) : ControllerBase
{
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
            new ResourceDto(resource.Value.Type, resource.Value.Environment, resource.Value.Criticality),
            request.Action,
            cancellationToken);

        if (result.Decision == AuthorizationDecision.ALLOW)
        {
            result = result with
            {
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(request.SessionDurationMinutes)
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