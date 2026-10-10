using System.Linq.Expressions;
using Analytics.Service.Data;
using Analytics.Service.DTOs;
using Analytics.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace Analytics.Service.Services;

/// <summary>
/// Read-side aggregation over the Analytics Service's own event store.
///
/// Design rules honoured here:
/// <list type="bullet">
///   <item>Filtering and grouping happen in PostgreSQL through EF Core; the only
///         data materialised in memory is the already-aggregated result set.</item>
///   <item>Reads are <c>AsNoTracking</c> and fully async with cancellation.</item>
///   <item>Every ordering has a deterministic secondary key so repeated calls with
///         identical data always produce byte-identical responses.</item>
///   <item>A valid range with no data returns zeros and empty collections, never
///         an error.</item>
/// </list>
/// </summary>
public sealed class AnalyticsService(AnalyticsDbContext dbContext) : IAnalyticsService
{
    /// <summary>Percentages are reported to 2 decimal places throughout.</summary>
    private const int PercentageDecimals = 2;

    // Materialized arrays: EF Core translates a captured-array Contains into SQL
    // IN (...) and parameterizes the values, which keeps the query sargable
    // against IX_AnalyticsEvents_EventType_OccurredAt.
    private static readonly string[] RequestEventTypes = [.. AnalyticsMetricMap.RequestEventTypes];
    private static readonly string[] ApprovalEventTypes = [.. AnalyticsMetricMap.ApprovalEventTypes];
    private static readonly string[] DenialEventTypes = [.. AnalyticsMetricMap.DenialEventTypes];
    private static readonly string[] RevocationEventTypes = [.. AnalyticsMetricMap.RevocationEventTypes];
    private static readonly string[] RevocationSourceTopics = [.. AnalyticsMetricMap.SourceTopicRevocationTopics];

    public async Task<AnalyticsSummaryResponse> GetSummaryAsync(
        AnalyticsDateRange range,
        CancellationToken cancellationToken = default)
    {
        // One grouped query produces both the per-day trend and - by summing the
        // returned buckets - the totals. Deriving totals from the same grouped
        // rows is what guarantees "totals == sum(trend)" by construction rather
        // than by convention.
        //
        // The projection targets an anonymous shape and is mapped to the DTO
        // afterwards: that keeps every filter, group-by and conditional
        // aggregate in SQL while avoiding provider-specific limitations on
        // projecting straight into a positional record.
        var buckets = await BuildMetricQuery(range)
            .GroupBy(e => e.OccurredAtDate)
            .Select(g => new
            {
                Date = g.Key,
                Requests = g.Sum(e => RequestEventTypes.Contains(e.EventType) ? 1 : 0),
                Approvals = g.Sum(e => ApprovalEventTypes.Contains(e.EventType) ? 1 : 0),
                Denials = g.Sum(e => DenialEventTypes.Contains(e.EventType) ? 1 : 0),
                Revocations = g.Sum(e => (RevocationEventTypes.Contains(e.EventType)
                                           || RevocationSourceTopics.Contains(e.SourceTopic)) ? 1 : 0)
            })
            .OrderBy(bucket => bucket.Date)
            .ToListAsync(cancellationToken);

        var trend = buckets
            .Select(bucket => new AnalyticsTrendPoint(
                bucket.Date,
                bucket.Requests,
                bucket.Approvals,
                bucket.Denials,
                bucket.Revocations))
            .ToList();

        var totals = new AnalyticsTotals(
            Requests: trend.Sum(point => point.Requests),
            Approvals: trend.Sum(point => point.Approvals),
            Denials: trend.Sum(point => point.Denials),
            Revocations: trend.Sum(point => point.Revocations));

        return new AnalyticsSummaryResponse(range.Start, range.End, totals, trend);
    }

    public async Task<TopResourcesResponse> GetTopResourcesAsync(
        AnalyticsDateRange range,
        int limit,
        CancellationToken cancellationToken = default)
    {
        // AC-3: ranked by ApprovalRequested only. AccessRequested and
        // AccessGranted are authorization-engine decisions and are excluded so
        // they cannot inflate the user-submitted request total.
        var requestQuery = ApplyRange(
            dbContext.AnalyticsEvents.AsNoTracking(),
            range,
            AnalyticsMetricMap.RequestEventTypes);

        var totalRequests = await requestQuery.CountAsync(cancellationToken);

        if (totalRequests == 0)
        {
            return new TopResourcesResponse(range.Start, range.End, 0, []);
        }

        // ResourceName is resolved at ingestion (Metadata.ResourceName, else the
        // Resource identifier), so grouping on it implements the required
        // "prefer name, fall back to identifier" rule in a single indexable step.
        var grouped = await requestQuery
            .GroupBy(e => e.ResourceName)
            .Select(g => new
            {
                ResourceName = g.Key,
                ResourceId = g.Max(e => e.Resource),
                RequestCount = g.Count()
            })
            .OrderByDescending(item => item.RequestCount)
            // Deterministic secondary sort: ties are broken by resource name and
            // then id, so repeated calls over identical data always return the
            // same order. A comparer cannot be supplied here because it would
            // stop the ordering being translatable to SQL.
            .ThenBy(item => item.ResourceName)
            .ThenBy(item => item.ResourceId)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var items = new List<TopResourceResponse>(grouped.Count);
        var rank = 1;

        foreach (var group in grouped)
        {
            // Denominator is the total ApprovalRequested count in range, not the
            // sum of the returned (possibly limited) items.
            items.Add(new TopResourceResponse(
                Rank: rank++,
                ResourceId: group.ResourceId,
                ResourceName: group.ResourceName,
                RequestCount: group.RequestCount,
                Percentage: CalculatePercentage(group.RequestCount, totalRequests)));
        }

        return new TopResourcesResponse(range.Start, range.End, totalRequests, items);
    }
    public async Task<DenialReasonsResponse> GetDenialReasonsAsync(
        AnalyticsDateRange range,
        int limit,
        CancellationToken cancellationToken = default)
    {
        // AC-4: AccessDenied (Metadata.Reason) + ApprovalRejected
        // (Metadata.RejectionReason). The reason is normalized at ingestion and
        // COALESCEd here as a belt-and-braces guard so a legacy null can never
        // fragment the distribution.
        var denialQuery = ApplyRange(
            dbContext.AnalyticsEvents.AsNoTracking(),
            range,
            AnalyticsMetricMap.DenialEventTypes);

        var totalDenials = await denialQuery.CountAsync(cancellationToken);

        if (totalDenials == 0)
        {
            return new DenialReasonsResponse(range.Start, range.End, 0, []);
        }

        var grouped = await denialQuery
            .GroupBy(e => e.DenialReason ?? AnalyticsMetricMap.UnknownDenialReason)
            .Select(g => new
            {
                Reason = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(item => item.Count)
            // Deterministic tie-break by reason name (no comparer: it would not
            // translate to SQL).
            .ThenBy(item => item.Reason)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var items = grouped
            .Select(item => new DenialReasonResponse(
                Reason: item.Reason,
                Count: item.Count,
                Percentage: CalculatePercentage(item.Count, totalDenials)))
            .ToList();

        return new DenialReasonsResponse(range.Start, range.End, totalDenials, items);
    }

    /// <summary>
    /// Base query for the summary: the date range restricted to the event types
    /// that contribute to at least one headline metric. This keeps logins and
    /// resource-change events out of the numbers and guarantees that every
    /// emitted trend bucket is meaningful.
    /// </summary>
    private IQueryable<AnalyticsEvent> BuildMetricQuery(AnalyticsDateRange range) =>
        ApplyRange(dbContext.AnalyticsEvents.AsNoTracking(), range, AnalyticsMetricMap.AllMetricEventTypes);

    private static IQueryable<AnalyticsEvent> ApplyRange(
        IQueryable<AnalyticsEvent> query,
        AnalyticsDateRange range,
        IReadOnlyCollection<string> eventTypes)
    {
        // An IN-list keeps the query sargable against IX_AnalyticsEvents_EventType_OccurredAt.
        query = query.Where(e => eventTypes.Contains(e.EventType));

        if (range.Start is { } start)
        {
            query = query.Where(e => e.OccurredAt >= start);
        }

        if (range.End is { } end)
        {
            query = query.Where(e => e.OccurredAt <= end);
        }

        return query;
    }

    /// <summary>
    /// count / total * 100 rounded half-away-from-zero to 2 decimals.
    /// Returns 0 when the denominator is 0, so a zero-data response can never
    /// divide by zero.
    /// </summary>
    internal static decimal CalculatePercentage(int count, int total) =>
        total == 0
            ? 0m
            : Math.Round((decimal)count / total * 100m, PercentageDecimals, MidpointRounding.AwayFromZero);
}
