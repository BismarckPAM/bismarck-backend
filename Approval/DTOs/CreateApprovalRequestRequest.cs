using System.ComponentModel.DataAnnotations;

namespace Approval.Service.DTOs;

public sealed class CreateApprovalRequestRequest
{
    [Required]
    [StringLength(100)]
    public string ResourceId { get; set; } = string.Empty;

    [Range(1, 5)]
    public int RequestedLevel { get; set; }

    [Required]
    [StringLength(500)]
    public string Reason { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int DurationMinutes { get; set; }
}
