namespace Approval.Service.DTOs;

public sealed class CreateApprovalRequestRequest
{
    public string ResourceId { get; set; } = string.Empty;
    public int RequestedLevel { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
}
