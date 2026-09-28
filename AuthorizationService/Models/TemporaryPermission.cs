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
    // expiry worker can remove the exact grant it created.
    public string? CloudRoleAssignmentId { get; set; }
    public string? ProvisioningStatus { get; set; }
    public string? ProvisioningDetail { get; set; }
}

public enum TemporaryPermissionStatus
{
    ACTIVE,
    EXPIRED,
    REVOKED
}