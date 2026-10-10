namespace Analytics.Service.DTOs;

/// <summary>
/// One daily UTC bucket in the summary time series. Only days that saw at least
/// one metric-bearing event are emitted, which keeps the series sparse but makes
/// <c>sum(trend)</c> exactly equal to <c>totals</c>.
/// </summary>
public sealed record AnalyticsTrendPoint(
    DateOnly Date,
    int Requests,
    int Approvals,
    int Denials,
    int Revocations);
