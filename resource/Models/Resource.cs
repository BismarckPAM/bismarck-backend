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

    // ---- Azure virtual-machine targeting (optional) -------------------------
    // Set these on a resource of Type "VM" so the JIT provisioner can grant a
    // role assignment scoped to THAT machine rather than a whole resource
    // group, and so the console can show the user how to connect.
    public string? AzureVmName { get; set; }

    public string? AzureResourceGroup { get; set; }

    /// <summary>
    /// Full ARM resource ID of the VM. When set it takes precedence over
    /// AzureVmName/AzureResourceGroup, e.g.
    /// /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Compute/virtualMachines/{name}
    /// </summary>
    public string? AzureResourceId { get; set; }

    /// <summary>"Linux" or "Windows" - selects the connection hint shown in the console.</summary>
    public string? OsType { get; set; }

    /// <summary>Public IP address or FQDN used to reach the VM.</summary>
    public string? PublicHost { get; set; }
}
