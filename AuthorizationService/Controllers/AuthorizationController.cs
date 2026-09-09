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
        var user = await identityServiceClient.GetUserRoleAsync(
            request.UserId,
            cancellationToken: cancellationToken);
        if (user is null)
            return Ok(AuthorizationDecisionResult.Deny("USER_UNVERIFIED"));

        if (!user.IsActive)
            return Ok(AuthorizationDecisionResult.Deny("USER_DEACTIVATED"));

        if (string.IsNullOrWhiteSpace(user.RoleName))
            return Ok(AuthorizationDecisionResult.Deny("USER_ROLE_NOT_ASSIGNED"));

        var resource = await resourceServiceClient.GetResourceContextAsync(
            request.ResourceId,
            cancellationToken: cancellationToken);
        if (resource is null)
            return Ok(AuthorizationDecisionResult.Deny("RESOURCE_NOT_FOUND"));

        var result = await policyDecisionEngine.EvaluateAsync(
            new UserDto(user.Id, user.RoleName, user.IsActive),
            new ResourceDto(resource.Type, resource.Environment, resource.Criticality),
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