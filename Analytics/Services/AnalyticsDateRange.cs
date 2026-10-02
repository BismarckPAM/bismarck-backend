using System.Globalization;

namespace Analytics.Service.Services;

/// <summary>
/// A validated, UTC-normalized date range. <c>null</c> bounds mean "unbounded on
/// that side", so a range with neither bound covers all currently stored history.
/// </summary>
public sealed record AnalyticsDateRange(DateTimeOffset? Start, DateTimeOffset? End)
{
    /// <summary>Range covering every stored event, used when no dates are supplied.</summary>
    public static AnalyticsDateRange All => new(null, null);
}

/// <summary>Outcome of parsing the startDate/endDate query parameters.</summary>
public sealed record AnalyticsDateRangeParseResult(
    AnalyticsDateRange Range,
    IDictionary<string, string[]> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Parses and validates the ISO-8601 <c>startDate</c> / <c>endDate</c> parameters
/// shared by all three analytics endpoints.
///
/// Rules (BIS-402 AC-5 / DoD-3):
/// <list type="bullet">
///   <item>neither supplied  -> unbounded range (all available history)</item>
///   <item>only startDate    -> OccurredAt &gt;= startDate</item>
///   <item>only endDate      -> OccurredAt &lt;= endDate</item>
///   <item>both              -> inclusive range</item>
///   <item>startDate &gt; endDate -> validation error (never silently swapped)</item>
///   <item>malformed value   -> validation error</item>
/// </list>
/// Every parsed value is normalized to UTC so comparisons are offset-safe.
/// </summary>
public static class AnalyticsDateRangeParser
{
    public static AnalyticsDateRangeParseResult Parse(string? startDate, string? endDate)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        var start = ParseBound(startDate, "startDate", errors);
        var end = ParseBound(endDate, "endDate", errors);

        // Only compare once both bounds parsed successfully, otherwise the caller
        // would receive two competing errors for a single bad input.
        if (start is not null && end is not null && start > end)
        {
            errors["startDate"] =
            [
                $"startDate ({start:O}) must be less than or equal to endDate ({end:O})."
            ];
        }

        var range = errors.Count == 0
            ? new AnalyticsDateRange(start, end)
            : AnalyticsDateRange.All;

        return new AnalyticsDateRangeParseResult(range, errors);
    }

    private static DateTimeOffset? ParseBound(
        string? raw,
        string parameterName,
        IDictionary<string, string[]> errors)
    {
        // A missing or whitespace-only parameter means "unbounded", not an error.
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            // Normalize to UTC so a caller sending +05:00 and the database's
            // timestamptz values are compared on the same timeline.
            return parsed.ToUniversalTime();
        }

        errors[parameterName] =
        [
            $"'{raw}' is not a valid ISO-8601 date. Expected a value such as 2026-10-01T00:00:00Z."
        ];

        return null;
    }
}
