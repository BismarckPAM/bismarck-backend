using System.Data.Common;
using Analytics.Service.Data;
using Analytics.Service.Models;
using Analytics.Service.Services;
using Messaging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Analytics.Service.Tests;

/// <summary>
/// Aggregation behaviour: SUMMARY, TOP RESOURCES and DENIAL REASONS
/// (BIS-402 scenarios 8-30). Exercises the real AnalyticsService against a
/// SQLite in-memory database so the actual LINQ is translated to SQL, with no
/// Kafka broker and no external database required. PostgreSQL-specific
/// behaviour is additionally covered by the integration suite.
/// </summary>
public class AnalyticsServiceTests : IDisposable
{
    internal static readonly DateTimeOffset October1 = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset October2 = new(2026, 10, 2, 14, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset October3 = new(2026, 10, 3, 6, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset October15 = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset October20 = new(2026, 10, 20, 8, 30, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset November5 = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly SqliteAnalyticsDbContext _dbContext;

    public AnalyticsServiceTests()
    {
        // A private in-memory database per test instance keeps them isolated
        // and runs entirely in-process.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _dbContext = new SqliteAnalyticsDbContext(_connection);
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private AnalyticsService CreateService() => new(_dbContext);

    private void Seed(params AnalyticsEvent[] events) => _dbContext.AnalyticsEvents.AddRange(events);

    // =====================================================================
    // SUMMARY
    // =====================================================================

    [Fact] // 8. empty database -> zero totals
    public async Task Summary_EmptyDatabase_ReturnsZeroTotalsAndEmptyTrend()
    {
        var result = await CreateService().GetSummaryAsync(AnalyticsDateRange.All);

        Assert.Equal(0, result.Totals.Requests);
        Assert.Equal(0, result.Totals.Approvals);
        Assert.Equal(0, result.Totals.Denials);
        Assert.Equal(0, result.Totals.Revocations);
        Assert.Empty(result.Trend);
    }

    [Fact] // 9. ApprovalRequested increments requests
    public async Task Summary_ApprovalRequested_IncrementsRequests()
    {
        Seed(
            AnalyticsEventFactory.ApprovalRequested(October1),
            AnalyticsEventFactory.ApprovalRequested(October1),
            AnalyticsEventFactory.ApprovalRequested(October2));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetSummaryAsync(AnalyticsDateRange.All);

        Assert.Equal(3, result.Totals.Requests);
        Assert.Equal(0, result.Totals.Approvals);
    }

    [Fact] // 10. ApprovalGranted increments approvals
    public async Task Summary_ApprovalGranted_IncrementsApprovals()
    {
        Seed(AnalyticsEventFactory.ApprovalGranted(October15));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetSummaryAsync(AnalyticsDateRange.All);

        Assert.Equal(1, result.Totals.Approvals);
    }
    [Fact] // 11. AccessDenied increments denials
    public async Task Summary_AccessDenied_IncrementsDenials()
    {
        Seed(AnalyticsEventFactory.AccessDenied(October15, "INSUFFICIENT_ROLE_PERMISSIONS"));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetSummaryAsync(AnalyticsDateRange.All);

        Assert.Equal(1, result.Totals.Denials);
    }

    [Fact] // 12. ApprovalRejected increments denials
    public async Task Summary_ApprovalRejected_IncrementsDenials()
    {
        Seed(AnalyticsEventFactory.ApprovalRejected(October15, "Not justified"));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetSummaryAsync(AnalyticsDateRange.All);

        Assert.Equal(1, result.Totals.Denials);
    }

    [Fact] // 13. PermissionRevoked increments revocations
    public async Task Summary_PermissionRevoked_IncrementsRevocations()
    {
        Seed(AnalyticsEventFactory.PermissionRevoked(October15));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetSummaryAsync(AnalyticsDateRange.All);

        Assert.Equal(1, result.Totals.Revocations);
    }

    [Fact] // 14. JitRevoked (admin manual revoke) increments revocations
    public async Task Summary_JitRevokedTopic_IncrementsRevocations()
    {
        // The Authorization Service publishes the admin JIT revoke to the
        // jit-revoked topic while still stamping EventType = PermissionRevoked.
        Seed(AnalyticsEventFactory.PermissionRevoked(October15, KafkaTopics.JitRevoked));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetSummaryAsync(AnalyticsDateRange.All);

        Assert.Equal(1, result.Totals.Revocations);
    }

    [Fact] // 15. AccessGranted must NOT increment approvals
    public async Task Summary_AccessGranted_DoesNotCountAsApproval()
    {
        Seed(
            AnalyticsEventFactory.AccessGranted(October15),
            AnalyticsEventFactory.AccessGranted(October15),
            AnalyticsEventFactory.ApprovalGranted(October15));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetSummaryAsync(AnalyticsDateRange.All);

        Assert.Equal(1, result.Totals.Approvals);
    }

    [Fact] // 16. AccessRequested must NOT inflate the user request total
    public async Task Summary_AccessRequested_DoesNotCountAsRequest()
    {
        Seed(
            AnalyticsEventFactory.AccessRequested(October15),
            AnalyticsEventFactory.AccessRequested(October15),
            AnalyticsEventFactory.ApprovalRequested(October15));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetSummaryAsync(AnalyticsDateRange.All);

        Assert.Equal(1, result.Totals.Requests);
    }

    [Fact] // 17. date range excludes events outside the window
    public async Task Summary_DateRange_ExcludesEventsOutsideWindow()
    {
        Seed(
            AnalyticsEventFactory.ApprovalRequested(October1),
            AnalyticsEventFactory.ApprovalRequested(October15),
            AnalyticsEventFactory.ApprovalRequested(November5));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetSummaryAsync(
            new AnalyticsDateRange(October1, October20));

        Assert.Equal(2, result.Totals.Requests);
    }
    [Fact] // 18. trend grouping by UTC date is correct and ascending
    public async Task Summary_Trend_GroupsByUtcDateAscending()
    {
        Seed(
            AnalyticsEventFactory.ApprovalRequested(new DateTimeOffset(2026, 10, 2, 23, 59, 59, TimeSpan.Zero)),
            AnalyticsEventFactory.ApprovalRequested(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)),
            AnalyticsEventFactory.ApprovalGranted(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero)),
            AnalyticsEventFactory.AccessDenied(October3, "NOPE"),
            AnalyticsEventFactory.PermissionRevoked(new DateTimeOffset(2026, 10, 3, 7, 0, 0, TimeSpan.Zero)));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetSummaryAsync(AnalyticsDateRange.All);

        Assert.Equal(3, result.Trend.Count);

        Assert.Equal(new DateOnly(2026, 10, 1), result.Trend[0].Date);
        Assert.Equal(1, result.Trend[0].Requests);
        Assert.Equal(1, result.Trend[0].Approvals);

        Assert.Equal(new DateOnly(2026, 10, 2), result.Trend[1].Date);
        Assert.Equal(1, result.Trend[1].Requests);

        Assert.Equal(new DateOnly(2026, 10, 3), result.Trend[2].Date);
        Assert.Equal(1, result.Trend[2].Denials);
        Assert.Equal(1, result.Trend[2].Revocations);

        // DoD invariant: totals must equal the sum of the trend.
        Assert.Equal(result.Trend.Sum(p => p.Requests), result.Totals.Requests);
        Assert.Equal(result.Trend.Sum(p => p.Approvals), result.Totals.Approvals);
        Assert.Equal(result.Trend.Sum(p => p.Denials), result.Totals.Denials);
        Assert.Equal(result.Trend.Sum(p => p.Revocations), result.Totals.Revocations);
    }

    [Fact]
    public async Task Summary_StartDateOnly_FiltersLowerBound()
    {
        Seed(
            AnalyticsEventFactory.ApprovalRequested(October1),
            AnalyticsEventFactory.ApprovalRequested(October20));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetSummaryAsync(new AnalyticsDateRange(October15, null));

        Assert.Equal(1, result.Totals.Requests);
    }

    [Fact]
    public async Task Summary_EndDateOnly_FiltersUpperBound()
    {
        Seed(
            AnalyticsEventFactory.ApprovalRequested(October1),
            AnalyticsEventFactory.ApprovalRequested(October20));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetSummaryAsync(new AnalyticsDateRange(null, October15));

        Assert.Equal(1, result.Totals.Requests);
    }

    [Fact]
    public async Task Summary_NonMetricEvents_AreExcludedEntirely()
    {
        // A login event is stored but contributes to no metric and no trend day.
        Seed(AnalyticsEventFactory.Create(
            "security.auth.login",
            October15,
            KafkaTopics.IdentityEvents,
            outcome: "SUCCESS"));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetSummaryAsync(AnalyticsDateRange.All);

        Assert.Empty(result.Trend);
        Assert.Equal(0, result.Totals.Requests);
    }

    [Fact]
    public async Task Summary_EchoesResolvedRangeInResponse()
    {
        var result = await CreateService().GetSummaryAsync(
            new AnalyticsDateRange(October1, October20));

        Assert.Equal(October1, result.StartDate);
        Assert.Equal(October20, result.EndDate);
    }
    // =====================================================================
    // TOP RESOURCES
    // =====================================================================

    [Fact] // 19. groups ApprovalRequested events correctly
    public async Task TopResources_GroupsApprovalRequestedByResource()
    {
        Seed(
            AnalyticsEventFactory.ApprovalRequested(October1, "Production Database", "res-1"),
            AnalyticsEventFactory.ApprovalRequested(October1, "Production Database", "res-1"),
            AnalyticsEventFactory.ApprovalRequested(October1, "Billing API", "res-2"));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetTopResourcesAsync(AnalyticsDateRange.All, 10);

        Assert.Equal(3, result.TotalRequests);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Production Database", result.Items[0].ResourceName);
        Assert.Equal(2, result.Items[0].RequestCount);
        Assert.Equal("Billing API", result.Items[1].ResourceName);
        Assert.Equal(1, result.Items[1].RequestCount);
    }

    [Fact] // 20. orders highest request count first
    public async Task TopResources_OrdersByCountDescending()
    {
        Seed(
            AnalyticsEventFactory.ApprovalRequested(October1, "A", "res-a"),
            AnalyticsEventFactory.ApprovalRequested(October1, "B", "res-b"),
            AnalyticsEventFactory.ApprovalRequested(October1, "B", "res-b"),
            AnalyticsEventFactory.ApprovalRequested(October1, "B", "res-b"),
            AnalyticsEventFactory.ApprovalRequested(October1, "C", "res-c"),
            AnalyticsEventFactory.ApprovalRequested(October1, "C", "res-c"));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetTopResourcesAsync(AnalyticsDateRange.All, 10);

        Assert.Equal("B", result.Items[0].ResourceName);
        Assert.Equal("C", result.Items[1].ResourceName);
        Assert.Equal("A", result.Items[2].ResourceName);
        Assert.Equal([1, 2, 3], result.Items.Select(i => i.Rank));
    }

    [Fact] // 21. ResourceName metadata is preferred
    public async Task TopResources_PrefersResourceNameMetadata()
    {
        Seed(AnalyticsEventFactory.ApprovalRequested(October1, "Production Database", "res-1"));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetTopResourcesAsync(AnalyticsDateRange.All, 10);

        Assert.Equal("Production Database", result.Items[0].ResourceName);
        Assert.Equal("res-1", result.Items[0].ResourceId);
    }

    [Fact] // 22. falls back safely to the resource identifier
    public async Task TopResources_FallsBackToResourceIdentifier()
    {
        // No ResourceName in metadata -> the Resource id becomes the label.
        Seed(AnalyticsEventFactory.ApprovalRequested(October1, resourceName: null, resource: "res-1"));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetTopResourcesAsync(AnalyticsDateRange.All, 10);

        Assert.Equal("res-1", result.Items[0].ResourceName);
        Assert.Equal("res-1", result.Items[0].ResourceId);
    }

    [Fact] // 23. percentages are correct
    public async Task TopResources_PercentagesAreCorrect()
    {
        // 5 of 9 requests -> 55.56% after rounding to 2 decimals.
        for (var i = 0; i < 5; i++)
        {
            Seed(AnalyticsEventFactory.ApprovalRequested(October1, "A", "res-a"));
        }

        for (var i = 0; i < 4; i++)
        {
            Seed(AnalyticsEventFactory.ApprovalRequested(October1, "B", "res-b"));
        }

        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetTopResourcesAsync(AnalyticsDateRange.All, 10);

        Assert.Equal(9, result.TotalRequests);
        Assert.Equal(55.56m, result.Items[0].Percentage);
        Assert.Equal(44.44m, result.Items[1].Percentage);
        Assert.Equal(100m, result.Items.Sum(i => i.Percentage), 1);
    }

    [Fact] // 24. zero data returns an empty collection safely
    public async Task TopResources_ZeroData_ReturnsEmptyCollection()
    {
        var result = await CreateService().GetTopResourcesAsync(AnalyticsDateRange.All, 10);

        Assert.Equal(0, result.TotalRequests);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task TopResources_IgnoresAccessAndGrantEvents()
    {
        Seed(
            AnalyticsEventFactory.ApprovalRequested(October1, "A", "res-a"),
            AnalyticsEventFactory.AccessRequested(October1),
            AnalyticsEventFactory.AccessGranted(October1),
            AnalyticsEventFactory.ApprovalGranted(October1));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetTopResourcesAsync(AnalyticsDateRange.All, 10);

        Assert.Equal(1, result.TotalRequests);
    }

    [Fact]
    public async Task TopResources_LimitIsRespectedButTotalIsNot()
    {
        Seed(
            AnalyticsEventFactory.ApprovalRequested(October1, "A", "res-a"),
            AnalyticsEventFactory.ApprovalRequested(October1, "B", "res-b"),
            AnalyticsEventFactory.ApprovalRequested(October1, "C", "res-c"));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetTopResourcesAsync(AnalyticsDateRange.All, 2);

        Assert.Equal(3, result.TotalRequests);
        Assert.Equal(2, result.Items.Count);
    }
    // =====================================================================
    // DENIAL REASONS
    // =====================================================================

    [Fact] // 25-26 + 28: both denial sources, counts and percentages
    public async Task DenialReasons_MergesBothDenialSourcesWithCorrectPercentages()
    {
        // 5 AccessDenied + 4 ApprovalRejected = 9 denials.
        for (var i = 0; i < 5; i++)
        {
            Seed(AnalyticsEventFactory.AccessDenied(October15, "INSUFFICIENT_ROLE_PERMISSIONS"));
        }

        for (var i = 0; i < 4; i++)
        {
            Seed(AnalyticsEventFactory.ApprovalRejected(October15, "Not justified"));
        }

        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetDenialReasonsAsync(AnalyticsDateRange.All, 10);

        Assert.Equal(9, result.TotalDenials);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("INSUFFICIENT_ROLE_PERMISSIONS", result.Items[0].Reason);
        Assert.Equal(5, result.Items[0].Count);
        Assert.Equal(55.56m, result.Items[0].Percentage);
        Assert.Equal("Not justified", result.Items[1].Reason);
        Assert.Equal(4, result.Items[1].Count);
        Assert.Equal(44.44m, result.Items[1].Percentage);
    }

    [Fact] // 27. missing reason -> UNKNOWN
    public async Task DenialReasons_MissingReason_BecomesUnknown()
    {
        Seed(
            AnalyticsEventFactory.AccessDenied(October15, reason: null),
            AnalyticsEventFactory.ApprovalRejected(October15, rejectionReason: null),
            AnalyticsEventFactory.AccessDenied(October15, reason: "   "));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetDenialReasonsAsync(AnalyticsDateRange.All, 10);

        Assert.Equal(3, result.TotalDenials);
        Assert.Single(result.Items);
        Assert.Equal(AnalyticsMetricMap.UnknownDenialReason, result.Items[0].Reason);
        Assert.Equal(3, result.Items[0].Count);
        Assert.Equal(100m, result.Items[0].Percentage);
    }

    [Fact] // 29. ordering is deterministic (count desc, then reason asc)
    public async Task DenialReasons_OrderingIsDeterministic()
    {
        Seed(
            AnalyticsEventFactory.AccessDenied(October15, "B_REASON"),
            AnalyticsEventFactory.AccessDenied(October15, "A_REASON"),
            AnalyticsEventFactory.AccessDenied(October15, "A_REASON"),
            AnalyticsEventFactory.AccessDenied(October15, "C_REASON"));

        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetDenialReasonsAsync(AnalyticsDateRange.All, 10);

        Assert.Equal("A_REASON", result.Items[0].Reason);
        Assert.Equal(2, result.Items[0].Count);
        // B and C are tied on count, so the ordinal reason name breaks the tie.
        Assert.Equal("B_REASON", result.Items[1].Reason);
        Assert.Equal("C_REASON", result.Items[2].Reason);
    }

    [Fact] // 30. zero-denial range returns totalDenials = 0 without divide-by-zero
    public async Task DenialReasons_ZeroDenials_ReturnsZeroWithoutDivideByZero()
    {
        Seed(AnalyticsEventFactory.ApprovalRequested(October15));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetDenialReasonsAsync(AnalyticsDateRange.All, 10);

        Assert.Equal(0, result.TotalDenials);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task DenialReasons_IgnoresNonDenialEvents()
    {
        Seed(
            AnalyticsEventFactory.AccessRequested(October15),
            AnalyticsEventFactory.AccessGranted(October15),
            AnalyticsEventFactory.ApprovalGranted(October15),
            AnalyticsEventFactory.PermissionRevoked(October15));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetDenialReasonsAsync(AnalyticsDateRange.All, 10);

        Assert.Equal(0, result.TotalDenials);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task DenialReasons_RespectsDateRange()
    {
        Seed(
            AnalyticsEventFactory.AccessDenied(October1, "IN_RANGE"),
            AnalyticsEventFactory.AccessDenied(November5, "OUT_OF_RANGE"));
        await _dbContext.SaveChangesAsync();

        var result = await CreateService().GetDenialReasonsAsync(
            new AnalyticsDateRange(October1, October20),
            10);

        Assert.Equal(1, result.TotalDenials);
        Assert.Equal("IN_RANGE", Assert.Single(result.Items).Reason);
    }

    [Fact]
    public void CalculatePercentage_AvoidsDivideByZero()
    {
        Assert.Equal(0m, AnalyticsService.CalculatePercentage(0, 0));
        Assert.Equal(0m, AnalyticsService.CalculatePercentage(5, 0));
    }

    [Theory]
    [InlineData(5, 9, 55.56)]
    [InlineData(1, 1, 100.0)]
    [InlineData(1, 3, 33.33)]
    [InlineData(2, 3, 66.67)]
    [InlineData(0, 10, 0.0)]
    public void CalculatePercentage_IsMathematicallyCorrect(int count, int total, double expected)
    {
        Assert.Equal((decimal)expected, AnalyticsService.CalculatePercentage(count, total));
    }
}