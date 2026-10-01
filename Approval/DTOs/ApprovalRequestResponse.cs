using Approval.Service.Models;

namespace Approval.Service.DTOs;

public sealed class ApprovalRequestResponse
{
    public Guid Id { get; set; }
    public string RequesterUserId { get; set; } = string.Empty;
    public string? RequesterName { get; set; }
    public string? RequesterEmail { get; set; }
    public string ResourceId { get; set; } = string.Empty;
    public string? ResourceName { get; set; }
    public string? ResourceType { get; set; }
    public string? Action { get; set; }
    public int RequestedLevel { get; set; }

    // Azure VM targeting, copied from the request during enrichment so the
    // approval-granted event can carry it to the JIT consumer.
    public string? AzureVmName { get; set; }
    public string? AzureResourceGroup { get; set; }
    public string? OsType { get; set; }
    public string? PublicHost { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public ApprovalStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByUserId { get; set; }
    public string? RejectionReason { get; set; }
}