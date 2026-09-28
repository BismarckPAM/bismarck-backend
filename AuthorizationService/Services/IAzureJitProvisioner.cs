using AuthorizationService.Models;

namespace AuthorizationService.Services;

/// <summary>Outcome of a cloud-side provisioning call.</summary>
public sealed record JitProvisioningResult(
    bool Succeeded,
    string? RoleAssignmentId,
    string? Detail);

/// <summary>
/// Provisions/deprovisions the cloud-side role assignment backing a JIT
/// session. Implementations must be tolerant of transient cloud failures:
/// a failed cloud call never throws out to the request pipeline, it returns a
/// result describing the failure so the local state machine can still advance.
/// </summary>
public interface IAzureJitProvisioner
{
    /// <summary>Whether this provisioner is configured for a real cloud tenant.</summary>
    bool IsConfigured { get; }

    Task<JitProvisioningResult> GrantAsync(TemporaryPermission permission, CancellationToken cancellationToken = default);

    Task<JitProvisioningResult> RevokeAsync(TemporaryPermission permission, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fallback used when Azure is not configured (local dev / CI). It records the
/// intent without contacting any cloud API so the JIT lifecycle still works
/// end-to-end.
/// </summary>
public sealed class NoOpJitProvisioner(ILogger<NoOpJitProvisioner> logger) : IAzureJitProvisioner
{
    public bool IsConfigured => false;

    public Task<JitProvisioningResult> GrantAsync(
        TemporaryPermission permission,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Azure JIT provisioning is not configured; recording local-only grant for PermissionId {PermissionId}.",
            permission.Id);

        return Task.FromResult(new JitProvisioningResult(
            Succeeded: false,
            RoleAssignmentId: null,
            Detail: "Azure provisioning is not configured; local-only session."));
    }

    public Task<JitProvisioningResult> RevokeAsync(
        TemporaryPermission permission,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new JitProvisioningResult(
            Succeeded: false,
            RoleAssignmentId: null,
            Detail: "Azure provisioning is not configured; no cloud grant to remove."));
}