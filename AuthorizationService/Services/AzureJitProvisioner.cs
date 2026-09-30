using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using AuthorizationService.Models;

namespace AuthorizationService.Services;

/// <summary>
/// Provisions temporary Azure RBAC role assignments for JIT sessions via the
/// ARM REST API. All methods are fail-tolerant: transport/ARM errors are logged
/// and returned as an unsuccessful result, never thrown into the pipeline.
///
/// Authentication uses <see cref="DefaultAzureCredential"/>, so the same code
/// path works in both environments:
///   - Azure Container Apps with a system-assigned managed identity (preferred -
///     no App Registration, no client secret, nothing to rotate)
///   - Local development, where it falls back to AZURE_CLIENT_ID /
///     AZURE_CLIENT_SECRET / AZURE_TENANT_ID if they are set
/// </summary>
public sealed class AzureJitProvisioner : IAzureJitProvisioner
{
    // Well-known Azure RBAC role definition GUIDs (stable, documented by Microsoft).
    private const string VirtualMachineUserLoginRoleId = "fb879df8-f326-4884-b1e9-212f174751e1";
    private const string VirtualMachineContributorRoleId = "9980e02c-c2be-4d73-94e8-173b1dc7cf3c";

    private const string ArmEndpoint = "https://management.azure.com";
    private const string ArmScope = "https://management.azure.com/.default";
    private const string GraphScope = "https://graph.microsoft.com/.default";

    private readonly HttpClient _httpClient;
    private readonly ILogger<AzureJitProvisioner> _logger;
    private readonly AzureJitOptions _options;
    private readonly TokenCredential _credential;

    public AzureJitProvisioner(
        HttpClient httpClient,
        IConfiguration configuration,
        TokenCredential credential,
        ILogger<AzureJitProvisioner> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = AzureJitOptions.FromConfiguration(configuration);
        _credential = credential;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<JitProvisioningResult> GrantAsync(TemporaryPermission permission, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return new JitProvisioningResult(false, null, "Azure provisioning is not configured; local-only session.");

        try
        {
            var principalId = await ResolvePrincipalIdAsync(permission, cancellationToken);
            if (string.IsNullOrWhiteSpace(principalId))
                return new JitProvisioningResult(false, null, "Could not resolve the user's Azure principal id (email not linked).");

            var accessToken = await GetAccessTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(accessToken))
                return new JitProvisioningResult(false, null, "Could not obtain an Azure access token.");

            var roleDefinitionId = ResolveRoleDefinitionId(permission.RequestedLevel);
            var assignmentGuid = Guid.NewGuid().ToString();
            var scope = BuildScope(permission);
            // Persist the FULL ARM path, never the bare GUID: RevokeAsync rebuilds
            // the DELETE url as ArmEndpoint + CloudRoleAssignmentId, so a bare GUID
            // would produce a malformed url and silently never revoke.
            var assignmentPath = $"{scope}/providers/Microsoft.Authorization/roleAssignments/{assignmentGuid}";
            var url = $"{ArmEndpoint}{assignmentPath}?api-version=2022-04-01";

            var body = new
            {
                properties = new
                {
                    roleDefinitionId = $"/subscriptions/{_options.SubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{roleDefinitionId}",
                    principalId,
                    principalType = "User"
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Azure role assignment PUT failed with {Status} for PermissionId {PermissionId}: {Detail}",
                    response.StatusCode, permission.Id, detail);
                return new JitProvisioningResult(false, null, $"Azure returned {(int)response.StatusCode}.");
            }

            // Record the scope used, for the audit trail.
            permission.AzureScope = scope;
            _logger.LogInformation(
                "Provisioned Azure role assignment {AssignmentId} at scope {Scope} for PermissionId {PermissionId}.",
                assignmentPath, scope, permission.Id);
            return new JitProvisioningResult(true, assignmentPath, $"Role assignment created at scope {scope}.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Failed to provision Azure role assignment for PermissionId {PermissionId}.", permission.Id);
            return new JitProvisioningResult(false, null, "Azure provisioning failed.");
        }
    }

    public async Task<JitProvisioningResult> RevokeAsync(TemporaryPermission permission, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(permission.CloudRoleAssignmentId))
            return new JitProvisioningResult(false, null, "No cloud role assignment recorded for this session.");

        if (!IsConfigured)
            return new JitProvisioningResult(false, permission.CloudRoleAssignmentId, "Azure provisioning is not configured.");

        try
        {
            var accessToken = await GetAccessTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(accessToken))
                return new JitProvisioningResult(false, permission.CloudRoleAssignmentId, "Could not obtain an Azure access token.");

            var url = $"{ArmEndpoint}{permission.CloudRoleAssignmentId}?api-version=2022-04-01";
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Azure role assignment DELETE failed with {Status} for PermissionId {PermissionId}: {Detail}",
                    response.StatusCode, permission.Id, detail);
                return new JitProvisioningResult(false, permission.CloudRoleAssignmentId, $"Azure returned {(int)response.StatusCode}.");
            }

            _logger.LogInformation("Removed Azure role assignment {AssignmentId} for PermissionId {PermissionId}.",
                permission.CloudRoleAssignmentId, permission.Id);
            return new JitProvisioningResult(true, permission.CloudRoleAssignmentId, "Azure role assignment removed.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Failed to remove Azure role assignment for PermissionId {PermissionId}.", permission.Id);
            return new JitProvisioningResult(false, permission.CloudRoleAssignmentId, "Azure revocation failed.");
        }
    }

    private async Task<string?> ResolvePrincipalIdAsync(TemporaryPermission permission, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.PrincipalId))
            return _options.PrincipalId;

        var lookupValue = permission.UserEmail;
        if (string.IsNullOrWhiteSpace(lookupValue))
            return null;

        var accessToken = await GetGraphTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
            return null;

        var url = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(lookupValue)}?$select=id";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var payload = await response.Content.ReadFromJsonAsync<GraphUserDto>(cancellationToken: cancellationToken);
        return payload?.Id;
    }

    private string ResolveRoleDefinitionId(int requestedLevel)
        => requestedLevel >= 3 ? VirtualMachineContributorRoleId : VirtualMachineUserLoginRoleId;

    /// <summary>
    /// Resolves the narrowest scope the role assignment can be applied at:
    ///
    ///   1. the individual VM  (best - least privilege)
    ///   2. the VM's resource group
    ///   3. the configured resource group
    ///   4. the whole subscription (last resort)
    ///
    /// Scoping to a single VM is what makes this real PAM: revoking removes
    /// access to that one machine, not to everything in the resource group.
    /// </summary>
    private string BuildScope(TemporaryPermission permission)
    {
        var vmName = permission.TargetVmName;
        var resourceGroup = permission.TargetResourceGroup;

        if (!string.IsNullOrWhiteSpace(vmName) && !string.IsNullOrWhiteSpace(resourceGroup))
        {
            return $"/subscriptions/{_options.SubscriptionId}/resourceGroups/{resourceGroup}"
                 + $"/providers/Microsoft.Compute/virtualMachines/{vmName}";
        }

        if (!string.IsNullOrWhiteSpace(resourceGroup))
            return $"/subscriptions/{_options.SubscriptionId}/resourceGroups/{resourceGroup}";

        if (!string.IsNullOrWhiteSpace(_options.ResourceGroup))
            return $"/subscriptions/{_options.SubscriptionId}/resourceGroups/{_options.ResourceGroup}";

        return $"/subscriptions/{_options.SubscriptionId}";
    }

    private Task<string?> GetAccessTokenAsync(CancellationToken ct) => GetTokenAsync(ArmScope, ct);
    private Task<string?> GetGraphTokenAsync(CancellationToken ct) => GetTokenAsync(GraphScope, ct);

    /// <summary>
    /// Acquires a bearer token via <see cref="DefaultAzureCredential"/>, which
    /// resolves to the container's managed identity inside Azure and falls back to
    /// environment variables locally. Never throws: a failure is logged and
    /// reported as null so the caller can degrade to a local-only session.
    /// </summary>
    private async Task<string?> GetTokenAsync(string scope, CancellationToken cancellationToken)
    {
        try
        {
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { scope }),
                cancellationToken);

            return token.Token;
        }
        catch (CredentialUnavailableException exception)
        {
            // No managed identity on the Container App, and no client credentials
            // in the environment. This is the single most common misconfiguration,
            // so spell out the fix rather than failing opaquely.
            _logger.LogError(exception,
                "No Azure credential available for scope {Scope}. Enable a system-assigned "
                + "managed identity on this Container App and assign it the 'Virtual Machine "
                + "User Login' role, or set AZURE_CLIENT_ID / AZURE_CLIENT_SECRET / AZURE_TENANT_ID.",
                scope);
            return null;
        }
        catch (AuthenticationFailedException exception)
        {
            _logger.LogError(exception, "Azure authentication failed for scope {Scope}.", scope);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Unexpected failure acquiring an Azure token for {Scope}.", scope);
            return null;
        }
    }

    private sealed record RoleAssignmentDto(string? Id);
    private sealed record GraphUserDto(string? Id);
}

/// <summary>Strongly-typed Azure JIT provisioning configuration.</summary>
public sealed class AzureJitOptions
{
    public string? TenantId { get; init; }
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? SubscriptionId { get; init; }
    public string? ResourceGroup { get; init; }
    public string? PrincipalId { get; init; }

    /// <summary>
    /// True when a managed identity should be used. Defaults to true because it
    /// requires no App Registration and no client secret to rotate.
    /// </summary>
    public bool UseManagedIdentity { get; init; } = true;

    /// <summary>True when the legacy client-credentials trio is fully supplied.</summary>
    public bool HasClientSecret =>
        !string.IsNullOrWhiteSpace(TenantId)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);

    /// <summary>
    /// A subscription is always required to build an ARM scope. A credential is
    /// then either the container's managed identity (preferred) or a client secret.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SubscriptionId)
        && (UseManagedIdentity || HasClientSecret);

    public static AzureJitOptions FromConfiguration(IConfiguration configuration)
    {
        string? Env(string key, string configKey) => Environment.GetEnvironmentVariable(key) ?? configuration[configKey];

        return new AzureJitOptions
        {
            TenantId = Env("AZURE_TENANT_ID", "AzureJit:TenantId"),
            ClientId = Env("AZURE_CLIENT_ID", "AzureJit:ClientId"),
            ClientSecret = Env("AZURE_CLIENT_SECRET", "AzureJit:ClientSecret"),
            SubscriptionId = Env("AZURE_SUBSCRIPTION_ID", "AzureJit:SubscriptionId"),
            ResourceGroup = Env("AZURE_RESOURCE_GROUP", "AzureJit:ResourceGroup"),
            PrincipalId = Env("AZURE_PRINCIPAL_ID", "AzureJit:PrincipalId"),
            UseManagedIdentity = configuration.GetValue("AzureJit:UseManagedIdentity", true)
        };
    }
}