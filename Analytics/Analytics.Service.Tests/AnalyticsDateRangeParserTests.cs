using Analytics.Service.Services;

namespace Analytics.Service.Tests;

/// <summary>
/// DATE VALIDATION (BIS-402 AC-5 / DoD-3).
/// </summary>
public class AnalyticsDateRangeParserTests
{
    [Fact] // 1. no dates -> unbounded range
    public void Parse_NoDates_ReturnsUnboundedRange()
    {
        var result = AnalyticsDateRangeParser.Parse(null, null);

        Assert.True(result.IsValid);
        Assert.Null(result.Range.Start);
        Assert.Null(result.Range.End);
    }

    [Fact] // 2. valid startDate only
    public void Parse_StartDateOnly_IsValid()
    {
        var result = AnalyticsDateRangeParser.Parse("2026-10-01T00:00:00Z", null);

        Assert.True(result.IsValid);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), result.Range.Start);
        Assert.Null(result.Range.End);
    }

    [Fact] // 3. valid endDate only
    public void Parse_EndDateOnly_IsValid()
    {
        var result = AnalyticsDateRangeParser.Parse(null, "2026-10-31T23:59:59Z");

        Assert.True(result.IsValid);
        Assert.Null(result.Range.Start);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 23, 59, 59, TimeSpan.Zero), result.Range.End);
    }

    [Fact] // 4. valid startDate + endDate
    public void Parse_StartAndEndDate_IsValid()
    {
        var result = AnalyticsDateRangeParser.Parse("2026-10-01T00:00:00Z", "2026-10-31T23:59:59Z");

        Assert.True(result.IsValid);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), result.Range.Start);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 23, 59, 59, TimeSpan.Zero), result.Range.End);
    }

    [Fact] // 5. startDate > endDate -> error, never silently swapped
    public void Parse_StartAfterEnd_ReturnsValidationError()
    {
        var result = AnalyticsDateRangeParser.Parse("2026-10-31T00:00:00Z", "2026-10-01T00:00:00Z");

        Assert.False(result.IsValid);
        Assert.Contains("startDate", result.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact] // 6. malformed startDate -> error
    public void Parse_MalformedStartDate_ReturnsValidationError()
    {
        var result = AnalyticsDateRangeParser.Parse("not-a-date", null);

        Assert.False(result.IsValid);
        Assert.Contains("startDate", result.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact] // 7. malformed endDate -> error
    public void Parse_MalformedEndDate_ReturnsValidationError()
    {
        var result = AnalyticsDateRangeParser.Parse(null, "2026-13-45T99:99:99Z");

        Assert.False(result.IsValid);
        Assert.Contains("endDate", result.Errors.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_EqualStartAndEnd_IsInclusiveAndValid()
    {
        var instant = "2026-10-05T12:00:00Z";

        var result = AnalyticsDateRangeParser.Parse(instant, instant);

        Assert.True(result.IsValid);
        Assert.Equal(result.Range.Start, result.Range.End);
    }

    [Fact]
    public void Parse_BlankStrings_AreTreatedAsUnbounded()
    {
        var result = AnalyticsDateRangeParser.Parse("   ", "");

        Assert.True(result.IsValid);
        Assert.Null(result.Range.Start);
        Assert.Null(result.Range.End);
    }

    [Fact]
    public void Parse_OffsetTimestamp_IsNormalizedToUtc()
    {
        // 05:00 at +05:00 is the same instant as 00:00Z.
        var result = AnalyticsDateRangeParser.Parse("2026-10-01T05:00:00+05:00", null);

        Assert.True(result.IsValid);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), result.Range.Start);
        Assert.Equal(TimeSpan.Zero, result.Range.Start!.Value.Offset);
    }

    [Fact]
    public void Parse_DateOnlyString_IsAcceptedAsUtcMidnight()
    {
        var result = AnalyticsDateRangeParser.Parse("2026-10-01", null);

        Assert.True(result.IsValid);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), result.Range.Start);
    }

    [Fact]
    public void Parse_BothMalformed_ReportsBothParameters()
    {
        var result = AnalyticsDateRangeParser.Parse("bad-start", "bad-end");

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void Parse_OffsetComparisonIsUtcSafe()
    {
        // start is 2026-10-01T00:00Z; end at +05:00 is 2026-09-30T19:00Z, i.e. earlier.
        var result = AnalyticsDateRangeParser.Parse("2026-10-01T00:00:00Z", "2026-10-01T00:00:00+05:00");

        Assert.False(result.IsValid);
    }
}
