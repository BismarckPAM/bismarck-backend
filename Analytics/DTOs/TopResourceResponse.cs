namespace Analytics.Service.DTOs;

/// <summary>
/// A single ranked entry in the top-requested-resources list, built only from
/// ApprovalRequested events.
/// </summary>
/// <param name="Rank">1-based position in the ranking.</param>
/// <param name="ResourceId">
/// The SecurityEvent.Resource identifier when the producer supplied one,
/// otherwise the resolved resource name.
/// </param>
/// <param name="ResourceName">
/// Metadata.ResourceName when present, otherwise the Resource identifier.
/// </param>
/// <param name="RequestCount">ApprovalRequested events for this resource.</param>
/// <param name="Percentage">
/// RequestCount as a percentage of the total ApprovalRequested events in the
/// range, rounded to 2 decimal places.
/// </param>
public sealed record TopResourceResponse(
    int Rank,
    string? ResourceId,
    string? ResourceName,
    int RequestCount,
    decimal Percentage);
