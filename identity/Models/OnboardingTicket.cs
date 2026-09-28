namespace Identity.Service.Models;

/// <summary>
/// A self-service registration request submitted by an unauthenticated visitor.
/// It is not a user account: an administrator reviews and either approves it
/// (which provisions a real <see cref="User"/>) or rejects it.
/// </summary>
public class OnboardingTicket
{
    public const string StatusPending = "PENDING";
    public const string StatusApproved = "APPROVED";
    public const string StatusRejected = "REJECTED";

    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string RequestedRole { get; set; } = string.Empty;
    public string Justification { get; set; } = string.Empty;
    public string Status { get; set; } = StatusPending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public string? RejectionReason { get; set; }
    public Guid? ProvisionedUserId { get; set; }
}