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