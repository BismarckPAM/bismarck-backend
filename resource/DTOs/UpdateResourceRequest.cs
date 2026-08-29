using Resource.Service.Models;

namespace Resource.Service.DTOs;

public class UpdateResourceRequest
{
    public string Type { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public ResourceCriticality Criticality { get; set; }
    public bool IsActive { get; set; }
}
