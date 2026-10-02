namespace Analytics.Service.DTOs;

/// <summary>
/// A single entry in the denial-reason distribution, covering both
/// AccessDenied (authorization decision) and ApprovalRejected (human rejection).
/// </summary>
public sealed record DenialReasonResponse(
    string Reason,
    int Count,
    decimal Percentage);
