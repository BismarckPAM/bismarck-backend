using Analytics.Service.DTOs;

namespace Analytics.Service.Services;

/// <summary>
/// Read-side analytics queries. Every method is pure: the Kafka consumer writes
/// events, and this service only derives aggregates from what is already stored.
/// </summary>
public interface IAnalyticsService
{
    /// <summary>
    /// AC-2: total request / approval / denial / revocation counts for the range,
    /// plus the daily UTC trend the BIS-404 dashboard will chart.
    /// </summary>
    Task<AnalyticsSummaryResponse> GetSummaryAsync(
        AnalyticsDateRange range,
        CancellationToken cancellationToken = default);

    /// <summary>AC-3: most-requested resources, ranked, for the range.</summary>
    Task<TopResourcesResponse> GetTopResourcesAsync(
        AnalyticsDateRange range,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>AC-4: denial-reason distribution for the range.</summary>
    Task<DenialReasonsResponse> GetDenialReasonsAsync(
        AnalyticsDateRange range,
        int limit,
        CancellationToken cancellationToken = default);
}
