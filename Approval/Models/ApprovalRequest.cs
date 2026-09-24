namespace Approval.Service.Models;

public class ApprovalRequest
{
    public Guid Id { get; set; }
    public string RequesterUserId { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public int RequestedLevel { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.PENDING;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByUserId { get; set; }
    public string? RejectionReason { get; set; }
}

public enum ApprovalStatus
{
    PENDING,
    APPROVED,
    REJECTED
}