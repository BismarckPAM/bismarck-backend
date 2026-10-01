namespace Approval.Service.Models;

public class ApprovalRequest
{
    public Guid Id { get; set; }
    public string RequesterUserId { get; set; } = string.Empty;

    /// <summary>Best-effort, human-readable requester label (never authoritative).</summary>
    public string? RequesterName { get; set; }
    public string? RequesterEmail { get; set; }

    public string ResourceId { get; set; } = string.Empty;

    /// <summary>Best-effort, human-readable resource label, e.g. "VirtualMachine · Development".</summary>
    public string? ResourceName { get; set; }
    public string? ResourceType { get; set; }

    /// <summary>The action the requester wants to perform (e.g. "RDP", "SSH").</summary>
    public string? Action { get; set; }

    // ---- Azure VM targeting (best-effort enrichment) -------------------------
    // Resolved at approval time so the JIT consumer receives them on the
    // `approval-granted` event instead of having to call the Resource Service,
    // which it cannot do without a bearer token.
    public string? AzureVmName { get; set; }
    public string? AzureResourceGroup { get; set; }
    public string? OsType { get; set; }
    public string? PublicHost { get; set; }

    public int RequestedLevel { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.PENDING;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByUserId { get; set; }
    public string? RejectionReason { get; set; }
}

public enum ApprovalStatus
{
    PENDING,
    APPROVED,
    REJECTED,
    EXPIRED,
    REVOKED
}