using System.ComponentModel.DataAnnotations;

namespace Approval.Service.DTOs;

public sealed class RejectApprovalRequest
{
    [Required]
    [StringLength(500)]
    public string Reason { get; set; } = string.Empty;
}