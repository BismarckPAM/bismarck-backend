using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Approval.Service.Clients;

/// <summary>Best-effort, human-readable identity context.</summary>
public sealed record IdentityContext(string UserId, string? Name, string? Email);

/// <summary>Best-effort, human-readable resource context.</summary>
public sealed record ResourceContext(
    string ResourceId,
    string Type,
    string Environment,
    // Azure VM targeting, so the JIT provisioner can scope the role assignment
    // to the machine and the console can show a connect command. Null for
    // non-VM resources.
    string? AzureVmName = null,
    string? AzureResourceGroup = null,
    string? OsType = null,
    string? PublicHost = null)
{
    /// <summary>Human-readable label, e.g. "VirtualMachine · Development".</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Type) && string.IsNullOrWhiteSpace(Environment)
            ? ResourceId
            : $"{Type} · {Environment}";
}

public interface IIdentityContextClient
{
    /// <summary>
    /// Resolves a user's friendly name/email. Returns <c>null</c> when the
    /// identity service is unreachable — enrichment is never allowed to block
    /// or fail request submission.
    /// </summary>
    Task<IdentityContext?> GetUserAsync(string userId, CancellationToken cancellationToken = default);
}

public interface IResourceContextClient
{
    /// <summary>
    /// Resolves a resource's type/environment. Returns <c>null</c> on failure
    /// (see <see cref="IIdentityContextClient"/>).
    /// </summary>
    Task<ResourceContext?> GetResourceAsync(string resourceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Shared best-effort HTTP plumbing: forwards the caller's bearer token and
/// swallows transport errors so a missing enrichment dependency degrades to
/// <c>null</c> instead of surfacing an error to the user.
/// </summary>
public abstract class ContextClientBase(HttpClient httpClient, IHttpContextAccessor httpContextAccessor)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected async Task<T?> TryGetAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);

            var authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authorization)
                && AuthenticationHeaderValue.TryParse(authorization, out var header))
            {
                request.Headers.Authorization = header;
            }

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}

public sealed class IdentityContextClient(HttpClient httpClient, IHttpContextAccessor httpContextAccessor)
    : ContextClientBase(httpClient, httpContextAccessor), IIdentityContextClient
{
    public async Task<IdentityContext?> GetUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var dto = await TryGetAsync<UserDto>($"/api/identity/users/{Uri.EscapeDataString(userId)}", cancellationToken);

        return dto is null ? null : new IdentityContext(dto.Id, dto.FullName, dto.Email);
    }

    // Matches Identity.Service.DTOs.UserResponse (FullName, not Name).
    private sealed record UserDto(string Id, string? FullName, string? Email);
}

public sealed class ResourceContextClient(HttpClient httpClient, IHttpContextAccessor httpContextAccessor)
    : ContextClientBase(httpClient, httpContextAccessor), IResourceContextClient
{
    public async Task<ResourceContext?> GetResourceAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        var dto = await TryGetAsync<ResourceDto>(
            $"/api/resources/{Uri.EscapeDataString(resourceId)}",
            cancellationToken);

        return dto is null ? null : new ResourceContext(
            dto.Id, dto.Type, dto.Environment,
            dto.AzureVmName, dto.AzureResourceGroup, dto.OsType, dto.PublicHost);
    }

    // Mirrors Resource.Service.DTOs.ResourceResponse, including the Azure VM
    // targeting fields added for JIT provisioning.
    private sealed record ResourceDto(
        string Id,
        string Type,
        string Environment,
        string? AzureVmName,
        string? AzureResourceGroup,
        string? OsType,
        string? PublicHost);
}