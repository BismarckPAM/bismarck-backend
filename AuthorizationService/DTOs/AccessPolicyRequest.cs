namespace AuthorizationService.DTOs;

public sealed class AccessPolicyRequest
{
    public string Role { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Criticality { get; set; } = string.Empty;
    public int MaxAccessLevel { get; set; }
    public bool RequiresApprovalForElevated { get; set; } = true;
}