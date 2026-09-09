namespace AuthorizationService.Clients;

public interface IResourceServiceClient
{
    Task<ResourceContext?> GetResourceContextAsync(
        Guid resourceId,
        string? accessToken = null,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceContext(
    string Type,
    string Environment,
    string Criticality);