namespace Approval.Service.DTOs;

public sealed class UpdateApprovalRequestRequest
{
    public int RequestedLevel { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
}
