using Approval.Service.Models;

namespace Approval.Service.DTOs;

public sealed class ApprovalRequestResponse
{
    public Guid Id { get; set; }
    public string RequesterUserId { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public int RequestedLevel { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public ApprovalStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByUserId { get; set; }
    public string? RejectionReason { get; set; }
}
