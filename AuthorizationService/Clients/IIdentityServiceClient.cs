using AuthorizationService.Models;

namespace AuthorizationService.Clients;

public interface IIdentityServiceClient
{
    Task<ServiceLookupResult<IdentityUser>> GetUserRoleAsync(
        Guid userId,
        string? accessToken = null,
        CancellationToken cancellationToken = default);
}

public sealed record IdentityUser(
    Guid Id,
    Guid RoleId,
    string RoleName,
    bool IsActive);