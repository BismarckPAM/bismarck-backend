using Audit.Service.DTOs;
using Audit.Service.Services;
using Microsoft.Data.Sqlite;

namespace Audit.Service.Tests;

/// <summary>
/// Covers the Audit Log viewer's query semantics that do not depend on
/// PostgreSQL-specific SQL: the explicit filters, the AND composition between
/// them, and the pagination contract.
/// <para>
/// The free-text <c>search</c> filter is intentionally NOT exercised here
/// because it uses <c>EF.Functions.ILike</c>, a PostgreSQL function. Running it
/// against SQLite would either throw or, worse, silently behave differently from
/// production. It is covered against real PostgreSQL in
/// <c>Audit.Service.IntegrationTests</c>.
/// </para>
/// </summary>
public sealed class AuditServiceQueryTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AuditDbContextHarness _harness = null!;

    // Seeded once and shared; every test only reads.
    internal static readonly DateTimeOffset October1 = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset October15 = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset November5 = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    private static IAuditService CreateService(AuditDbContextHarness harness) =>
        new AuditService(harness.Context);

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _harness = new AuditDbContextHarness(_connection);
        await _harness.Context.Database.EnsureCreatedAsync();

        _harness.Context.AuditLogs.AddRange(
            AuditLogFactory.Create("Alice Fernando", "Production Database",
                "security.access.granted", "SUCCESS", "GRANT", October1),
            AuditLogFactory.Create("Bob Stone", "Staging Database",
                "security.access.denied", "DENIED", "DENY", October15),
            AuditLogFactory.Create("Alice Fernando", "Development Laptop",
                "security.access.denied", "DENIED", "DENY", November5),
            AuditLogFactory.Create("Carol Nkemdirim", null,
                "security.auth.login", "SUCCESS", "LOGIN", October15));
        await _harness.Context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _harness.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private IAuditService Service => CreateService(_harness);

    // ---------------------------------------------------------------- paging

    [Fact] // scenario 1
    public async Task NoFilters_ReturnsPagedResults()
    {
        var result = await Service.GetLogsAsync(new AuditLogQueryParameters());

        Assert.Equal(4, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(4, result.Items.Count());
        Assert.Equal(1, result.TotalPages);
    }

    [Fact]
    public async Task Results_AreOrderedByOccurredAtDescending()
    {
        var result = await Service.GetLogsAsync(new AuditLogQueryParameters());

        var timestamps = result.Items.Select(i => i.OccurredAt).ToList();
        Assert.Equal(timestamps.OrderByDescending(t => t).ToList(), timestamps);
    }

    [Fact] // scenario 18 - identical timestamps must still produce a total order
    public async Task Ordering_IsDeterministic_WhenOccurredAtValuesAreIdentical()
    {
        var shared = new DateTimeOffset(2026, 12, 25, 8, 0, 0, TimeSpan.Zero);
        var ids = Enumerable.Range(1, 5).Select(_ => Guid.NewGuid()).ToList();

        await using (var extra = new AuditDbContextHarness(_connection))
        {
            extra.Context.AuditLogs.AddRange(
                ids.Select(id => AuditLogFactory.Create($"Actor{id:N}", "Res", occurredAt: shared, id: id)));
            await extra.Context.SaveChangesAsync();
        }

        var first = await Service.GetLogsAsync(new AuditLogQueryParameters(PageSize: 5));
        var second = await Service.GetLogsAsync(new AuditLogQueryParameters(PageSize: 5));

        // Id DESC is the tie-breaker, so the same rows come back in the same order.
        Assert.Equal(
            first.Items.Select(i => i.Id).OrderByDescending(i => i).ToList(),
            first.Items.Select(i => i.Id).ToList());
        Assert.Equal(second.Items.Select(i => i.Id), first.Items.Select(i => i.Id));
    }

    [Fact] // scenario 16 - count reflects the filtered set, not the page
    public async Task TotalCount_IsBasedOnTheFilteredResultBeforePagination()
    {
        var result = await Service.GetLogsAsync(
            new AuditLogQueryParameters(Outcome: "DENIED", PageSize: 1));

        Assert.Equal(2, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal(2, result.TotalPages);
    }

    [Fact] // scenario 17 - pages must not overlap for a stable dataset
    public async Task Page1AndPage2_DoNotContainDuplicateRows()
    {
        var page1 = await Service.GetLogsAsync(new AuditLogQueryParameters(Page: 1, PageSize: 2));
        var page2 = await Service.GetLogsAsync(new AuditLogQueryParameters(Page: 2, PageSize: 2));

        var ids1 = page1.Items.Select(i => i.Id).ToList();
        var ids2 = page2.Items.Select(i => i.Id).ToList();

        Assert.Empty(ids1.Intersect(ids2));
        Assert.Equal(2, ids1.Count);
        Assert.Equal(2, ids2.Count);
    }
// ------------------------------------------------------------- filtering

    [Fact] // scenario 6
    public async Task UserFilter_StillMatchesActorExactly()
    {
        var result = await Service.GetLogsAsync(new AuditLogQueryParameters(User: "Alice Fernando"));

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, i => Assert.Equal("Alice Fernando", i.Actor));
    }

    [Fact]
    public async Task UserFilter_RemainsExactAndCaseSensitive_UnchangedSemantics()
    {
        // The explicit filters intentionally keep their original exact-match
        // semantics; only the new `search` parameter is a partial match.
        var result = await Service.GetLogsAsync(new AuditLogQueryParameters(User: "alice fernando"));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task WhitespaceOnlyFilter_IsTreatedAsNotSupplied()
    {
        var result = await Service.GetLogsAsync(new AuditLogQueryParameters(User: "   "));

        Assert.Equal(4, result.TotalCount);
    }

    [Fact] // scenario 7
    public async Task ResourceFilter_StillMatchesResourceExactly()
    {
        var result = await Service.GetLogsAsync(
            new AuditLogQueryParameters(Resource: "Production Database"));

        Assert.Single(result.Items);
        Assert.Equal("Production Database", result.Items.Single().Resource);
    }

    [Fact]
    public async Task ResourceFilter_DoesNotMatchNullResource()
    {
        var result = await Service.GetLogsAsync(new AuditLogQueryParameters(Resource: "anything"));

        Assert.Empty(result.Items);
    }

    [Fact] // scenario 8
    public async Task EventTypeFilter_StillWorks()
    {
        var result = await Service.GetLogsAsync(
            new AuditLogQueryParameters(EventType: "security.auth.login"));

        Assert.Single(result.Items);
        Assert.Equal("Carol Nkemdirim", result.Items.Single().Actor);
    }

    [Fact] // scenario 9 - new in BIS-403
    public async Task OutcomeFilter_Works()
    {
        var result = await Service.GetLogsAsync(new AuditLogQueryParameters(Outcome: "DENIED"));

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, i => Assert.Equal("DENIED", i.Outcome));
    }

    [Fact] // scenario 10
    public async Task FromFilter_IsInclusive()
    {
        var result = await Service.GetLogsAsync(new AuditLogQueryParameters(From: October15));

        Assert.Equal(3, result.TotalCount);
    }

    [Fact] // scenario 11
    public async Task ToFilter_IsInclusive()
    {
        var result = await Service.GetLogsAsync(new AuditLogQueryParameters(To: October1));

        Assert.Single(result.Items);
        Assert.Equal(October1, result.Items.Single().OccurredAt);
    }

    [Fact] // scenario 12
    public async Task FromAndToRange_Works()
    {
        var result = await Service.GetLogsAsync(
            new AuditLogQueryParameters(From: October1, To: October15));

        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task RangeExcludingEverything_ReturnsNoRows()
    {
        var result = await Service.GetLogsAsync(new AuditLogQueryParameters(
            From: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            To: new DateTimeOffset(2020, 12, 31, 0, 0, 0, TimeSpan.Zero)));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    // ---------------------------------------------------------- composition

    [Fact] // scenario 15 - filters compose with AND
    public async Task Filters_ComposeWithAndSemantics()
    {
        var result = await Service.GetLogsAsync(new AuditLogQueryParameters(
            User: "Alice Fernando",
            Outcome: "DENIED"));

        var item = Assert.Single(result.Items);
        Assert.Equal("Development Laptop", item.Resource);
    }

    [Fact]
    public async Task ContradictoryFilters_ReturnNoRows()
    {
        var result = await Service.GetLogsAsync(new AuditLogQueryParameters(
            Outcome: "DENIED",
            EventType: "security.auth.login"));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task CombinedFilters_WithPaging_StillPageTheFilteredSet()
    {
        var page1 = await Service.GetLogsAsync(
            new AuditLogQueryParameters(Outcome: "DENIED", Page: 1, PageSize: 1));
        var page2 = await Service.GetLogsAsync(
            new AuditLogQueryParameters(Outcome: "DENIED", Page: 2, PageSize: 1));

        Assert.Equal(2, page1.TotalCount);
        Assert.NotEqual(page1.Items.Single().Id, page2.Items.Single().Id);
    }

    // ------------------------------------------- free-text search pattern

    [Theory]
    [InlineData("alice", "%alice%")]
    [InlineData("production", "%production%")]
    // Wildcards inside the user's term are escaped so they match literally
    // instead of degenerating into "match anything" patterns.
    [InlineData("100%", "%100\\%%")]
    [InlineData("a_b", "%a\\_b%")]
    [InlineData("back\\slash", "%back\\\\slash%")]
    public void BuildContainsPattern_WrapsAndEscapes(string search, string expected)
    {
        Assert.Equal(expected, AuditService.BuildContainsPattern(search));
    }

    [Fact]
    public async Task PageBeyondTheEnd_ReturnsEmptyItemsButKeepsTotalCount()
    {
        var result = await Service.GetLogsAsync(new AuditLogQueryParameters(Page: 99, PageSize: 2));

        Assert.Empty(result.Items);
        Assert.Equal(4, result.TotalCount);
    }

    [Theory] // page/pageSize sanitization
    [InlineData(0)]
    [InlineData(-5)]
    public async Task InvalidPaging_IsSanitized(int requestedPage)
    {
        var result = await Service.GetLogsAsync(
            new AuditLogQueryParameters(Page: requestedPage, PageSize: 0));

        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
    }

    [Fact]
    public async Task OversizedPageSize_FallsBackToTheDefault()
    {
        var result = await Service.GetLogsAsync(new AuditLogQueryParameters(PageSize: 5000));

        Assert.Equal(20, result.PageSize);
    }
}