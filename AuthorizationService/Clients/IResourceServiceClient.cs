using AuthorizationService.Models;

namespace AuthorizationService.Clients;

public interface IResourceServiceClient
{
    Task<ServiceLookupResult<ResourceContext>> GetResourceContextAsync(
        Guid resourceId,
        string? accessToken = null,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceContext(
    string Type,
    string Environment,
    string Criticality);