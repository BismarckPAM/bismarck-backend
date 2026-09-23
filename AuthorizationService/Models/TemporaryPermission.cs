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
}

public enum TemporaryPermissionStatus
{
    ACTIVE,
    EXPIRED,
    REVOKED
}
