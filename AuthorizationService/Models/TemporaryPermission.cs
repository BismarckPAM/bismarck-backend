namespace AuthorizationService.Models;

public sealed class TemporaryPermission
{
    public Guid Id { get; set; }
    public Guid ApprovalId { get; set; }
    public Guid UserId { get; set; }
    public Guid ResourceId { get; set; }
    public int RequestedLevel { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public TemporaryPermissionStatus Status { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }

    // ---- Enriched display context (best-effort, nullable) ------------------
    // Populated from the enriched ApprovalGranted payload so the JIT screen and
    // notifications can show human-readable context instead of raw UUIDs.
    public string? UserEmail { get; set; }
    public string? ResourceName { get; set; }
    public string? Action { get; set; }

    // ---- Cloud provisioning bookkeeping ------------------------------------
    // Id of the cloud (Azure) role assignment created for this session, so the
    // expiry worker can remove the exact grant it created. Always the FULL ARM
    // resource path (never a bare GUID) so DELETE targets the right URL.
    public string? CloudRoleAssignmentId { get; set; }
    public string? ProvisioningStatus { get; set; }
    public string? ProvisioningDetail { get; set; }

    // ---- Azure VM connection context ----------------------------------------
    // Resolved from the Resource catalog when the session is created, so the
    // console can show the user exactly how to reach the target machine while
    // the session is ACTIVE.
    public string? TargetVmName { get; set; }
    public string? TargetResourceGroup { get; set; }
    public string? TargetHost { get; set; }
    public string? TargetOsType { get; set; }

    /// <summary>
    /// Exact command the user should run to reach the VM, e.g.
    /// "ssh alice@20.51.0.4" or "az vm ssh -g rg -n vm -l alice".
    /// Null when the resource is not an Azure VM.
    /// </summary>
    public string? ConnectionCommand { get; set; }

    /// <summary>ARM scope the role assignment was applied at, for auditability.</summary>
    public string? AzureScope { get; set; }
}

public enum TemporaryPermissionStatus
{
    ACTIVE,
    EXPIRED,
    REVOKED
}