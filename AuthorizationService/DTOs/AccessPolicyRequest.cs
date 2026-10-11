using System.Text.Json.Serialization;

namespace AuthorizationService.DTOs;

public sealed class AccessPolicyRequest
{
    public string Role { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Criticality { get; set; } = string.Empty;
    [JsonRequired]
    public int MaxAccessLevel { get; set; }

    [JsonRequired]
    public bool RequiresApprovalForElevated { get; set; } = true;
}