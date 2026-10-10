namespace Analytics.Service.DTOs;

/// <summary>Response for <c>GET /api/analytics/denial-reasons</c>.</summary>
public sealed record DenialReasonsResponse(
    DateTimeOffset? StartDate,
    DateTimeOffset? EndDate,
    int TotalDenials,
    IReadOnlyList<DenialReasonResponse> Items);
