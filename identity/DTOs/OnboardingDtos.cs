namespace Identity.Service.DTOs;

/// <summary>Public payload submitted from the landing page registration dialog.</summary>
public class CreateOnboardingTicketRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string RequestedRole { get; set; } = string.Empty;
    public string Justification { get; set; } = string.Empty;

    /// <summary>Cloudflare Turnstile response token; verified server-side.</summary>
    public string TurnstileToken { get; set; } = string.Empty;
}

public class CreateOnboardingTicketResponse
{
    public Guid TicketId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class OnboardingTicketResponse
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string RequestedRole { get; set; } = string.Empty;
    public string Justification { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public string? RejectionReason { get; set; }
    public Guid? ProvisionedUserId { get; set; }
}

public class RejectOnboardingTicketRequest
{
    public string Reason { get; set; } = string.Empty;
}