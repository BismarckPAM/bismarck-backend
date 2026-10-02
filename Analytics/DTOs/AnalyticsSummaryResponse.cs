namespace Analytics.Service.DTOs;

/// <summary>
/// Response for <c>GET /api/analytics/summary</c>.
///
/// <para>
/// The <c>trend</c> array is included so the BIS-404 dashboard can chart
/// request / approval / denial / revocation movement over time without any
/// further backend change. It is a daily UTC time series built from the same
/// filtered event set as <c>totals</c>, so the totals are always exactly the sum
/// of the trend points.
/// </para>
/// </summary>
public sealed record AnalyticsSummaryResponse(
    DateTimeOffset? StartDate,
    DateTimeOffset? EndDate,
    AnalyticsTotals Totals,
    IReadOnlyList<AnalyticsTrendPoint> Trend);

/// <summary>The four canonical BIS-402 headline counts.</summary>
public sealed record AnalyticsTotals(
    int Requests,
    int Approvals,
    int Denials,
    int Revocations);
