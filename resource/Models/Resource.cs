namespace Resource.Service.Models;

public class Resource
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public ResourceCriticality Criticality { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
