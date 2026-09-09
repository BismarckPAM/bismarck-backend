namespace AuthorizationService.Models;

public sealed class AccessPolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Role { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Criticality { get; set; } = string.Empty;
    public int MaxAccessLevel { get; set; }
    public bool RequiresApprovalForElevated { get; set; } = true;
    public bool IsActive { get; set; } = true;
}