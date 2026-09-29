using Resource.Service.Models;

namespace Resource.Service.DTOs;

public class UpdateResourceRequest
{
    public string Type { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public ResourceCriticality? Criticality { get; set; }
    public bool? IsActive { get; set; }

    // Azure VM targeting (optional; used when Type is "VM").
    public string? AzureVmName { get; set; }
    public string? AzureResourceGroup { get; set; }
    public string? AzureResourceId { get; set; }
    public string? OsType { get; set; }
    public string? PublicHost { get; set; }
}
