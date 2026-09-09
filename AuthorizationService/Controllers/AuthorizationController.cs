using AuthorizationService.Clients;
using AuthorizationService.DTOs;
using AuthorizationService.Models;
using AuthorizationService.Services;
using Microsoft.AspNetCore.Mvc;

namespace AuthorizationService.Controllers;

[ApiController]
[Route("api/authorization")]
public sealed class AuthorizationController(
    IIdentityServiceClient identityServiceClient,
    IResourceServiceClient resourceServiceClient,
    IPolicyDecisionEngine policyDecisionEngine) : ControllerBase
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
            return Ok(AuthorizationDecisionResult.Deny(reason));
        }

        if (!user.Value.IsActive)
            return Ok(AuthorizationDecisionResult.Deny(AuthorizationDenialReason.USER_DEACTIVATED));

        if (string.IsNullOrWhiteSpace(user.Value.RoleName))
            return Ok(AuthorizationDecisionResult.Deny(AuthorizationDenialReason.USER_ROLE_NOT_ASSIGNED));

        var resource = await resourceTask;
        if (resource.Value is null)
        {
            var reason = resource.FailureReason ?? AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED;
            return Ok(AuthorizationDecisionResult.Deny(reason));
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

        return Ok(result);
    }
}