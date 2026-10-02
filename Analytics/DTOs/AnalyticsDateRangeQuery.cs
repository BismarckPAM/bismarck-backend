namespace Analytics.Service.DTOs;

/// <summary>
/// The <c>startDate</c> / <c>endDate</c> query-string contract shared by all
/// three analytics endpoints.
///
/// The values are bound as raw strings rather than as <c>DateTimeOffset?</c> so
/// that a malformed value produces an explicit, readable 400 validation
/// response (with the offending parameter named) instead of an opaque
/// model-binding failure.
/// </summary>
public sealed record AnalyticsDateRangeQuery(string? StartDate, string? EndDate);
