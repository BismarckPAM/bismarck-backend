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
    Guid Id,
    string Type,
    string Environment,
    string Criticality,
    // Azure VM targeting - null for non-VM resources.
    string? AzureVmName = null,
    string? AzureResourceGroup = null,
    string? AzureResourceId = null,
    string? OsType = null,
    string? PublicHost = null);