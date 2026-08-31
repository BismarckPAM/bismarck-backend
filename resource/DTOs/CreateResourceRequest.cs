using Resource.Service.Models;

namespace Resource.Service.DTOs;

public class CreateResourceRequest
{
    public string Type { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public ResourceCriticality? Criticality { get; set; }
}
