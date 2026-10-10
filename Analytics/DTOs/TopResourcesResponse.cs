namespace Analytics.Service.DTOs;

/// <summary>Response for <c>GET /api/analytics/top-resources</c>.</summary>
public sealed record TopResourcesResponse(
    DateTimeOffset? StartDate,
    DateTimeOffset? EndDate,
    int TotalRequests,
    IReadOnlyList<TopResourceResponse> Items);
