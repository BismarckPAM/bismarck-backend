using AuthorizationService.Models;

namespace AuthorizationService.Services;

public interface IPolicyDecisionEngine
{
    Task<AuthorizationDecisionResult> EvaluateAsync(
        UserDto user,
        ResourceDto resource,
        string action,
        CancellationToken cancellationToken = default);
}