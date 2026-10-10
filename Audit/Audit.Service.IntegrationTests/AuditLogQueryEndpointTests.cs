using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Audit.Service.DTOs;
using Audit.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace Audit.Service.IntegrationTests;

/// <summary>
/// End-to-end coverage of GET /api/audit/logs (BIS-403) against the real host and
/// a real PostgreSQL database: the free-text Actor/Resource ILIKE search, the
/// explicit filters, their AND composition, date-range validation, pagination
/// stability, authentication, and the unchanged response contract.
/// </summary>
public sealed class AuditLogQueryEndpointTests : IClassFixture<AuditApiFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static readonly DateTimeOffset October1 = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset October15 = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset October31Late = new(2026, 10, 31, 23, 59, 59, TimeSpan.Zero);
    internal static readonly DateTimeOffset November5 = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    private const string Resource = "Production Database";
    private const string EventType = "security.access.granted";
    private const string Outcome = "SUCCESS";

    private readonly AuditApiFixture _fixture;

    public AuditLogQueryEndpointTests(AuditApiFixture fixture)
    {
        _fixture = fixture;
    }

    private Task SeedAsync(params AuditLog[] entries) =>
        _fixture.WithDbContextAsync(async context =>
        {
            context.AuditLogs.AddRange(entries);
            await context.SaveChangesAsync();
        });

    /// <summary>The default dataset used by the search/filter scenarios.</summary>
    private Task SeedDefaultDatasetAsync() => SeedAsync(
        AuditLogEntryBuilder.Create("Alice Fernando", Resource, EventType, Outcome, "GRANT", October1),
        AuditLogEntryBuilder.Create("Bob Stone", "Staging Database", "security.access.denied", "DENIED", "DENY", October15),
        AuditLogEntryBuilder.Create("Alice Fernando", "Development Laptop", "security.access.denied", "DENIED", "DENY", November5),
        // A row with no resource at all: search must not blow up on it.
        AuditLogEntryBuilder.Create("Carol Nkemdirim", null, "security.auth.login", Outcome, "LOGIN", October31Late));

    private async Task<PagedResult<AuditLog>> GetAsync(string queryString)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/audit/logs{queryString}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<AuditLog>>(JsonOptions);
        Assert.NotNull(body);
        return body!;
    }

    // ------------------------------------------------------------------ auth

    [Fact] // scenario 19
    public async Task Query_WithoutToken_Returns401()
    {
        await _fixture.ResetAsync();
        using var client = _fixture.CreateClient();

        var response = await client.GetAsync("/api/audit/logs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Query_WithInvalidToken_Returns401()
    {
        await _fixture.ResetAsync();
        using var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not.a.jwt");

        var response = await client.GetAsync("/api/audit/logs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Query_WithValidToken_Returns200()
    {
        await _fixture.ResetAsync();
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/audit/logs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --------------------------------------------------- free-text ILIKE search

    [Fact] // scenario 1
    public async Task NoFilters_ReturnsPagedResults()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var result = await GetAsync(string.Empty);

        Assert.Equal(4, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(4, result.Items.Count());
    }

    [Fact] // scenario 2 - partial, case-insensitive Actor match
    public async Task Search_MatchesActor_PartiallyAndCaseInsensitively()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var result = await GetAsync("?search=alice");

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, i => Assert.Contains("Alice", i.Actor, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_MatchesActor_WithLowercaseQueryAgainstMixedCaseData()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var result = await GetAsync("?search=FERNANDO");

        Assert.Equal(2, result.TotalCount);
    }

    [Fact] // scenario 3 - partial Resource match, no exact identifier required
    public async Task Search_MatchesResource_PartiallyAndCaseInsensitively()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var result = await GetAsync("?search=production");

        var item = Assert.Single(result.Items);
        Assert.Equal(Resource, item.Resource);
    }

    [Fact] // scenario 4
    public async Task Search_DoesNotMatchUnrelatedRecords()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var result = await GetAsync("?search=zzz-no-such-value");

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact] // scenario 5 - the null-Resource row must not break the query
    public async Task Search_HandlesNullResourceSafely()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        // "Carol" only exists on Actor, on the row whose Resource is null.
        var result = await GetAsync("?search=carol");

        var item = Assert.Single(result.Items);
        Assert.Null(item.Resource);
    }

    [Fact]
    public async Task Search_MatchesActorOrResource_AcrossBothFields()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        // "alice" is only ever an Actor value; "database" is only ever part of a
        // Resource value. Both must be found, proving the OR spans the two columns.
        var byActor = await GetAsync("?search=alice");
        var byResource = await GetAsync("?search=database");

        Assert.Equal(2, byActor.TotalCount);
        Assert.All(byActor.Items, i => Assert.Contains("Alice", i.Actor, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(2, byResource.TotalCount);
        Assert.All(byResource.Items, i => Assert.Contains("Database", i.Resource!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_TrimsSurroundingWhitespace()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var result = await GetAsync("?search=%20alice%20");

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task Search_TreatsWildcardsAsLiteralText()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        // A bare '%' would match everything if it were passed through unescaped.
        var result = await GetAsync("?search=%25");

        Assert.Empty(result.Items);
    }

    // ------------------------------------------------------- explicit filters

    [Fact] // scenario 6
    public async Task UserFilter_StillWorks()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var result = await GetAsync("?user=Alice%20Fernando");

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, i => Assert.Equal("Alice Fernando", i.Actor));
    }

    [Fact] // scenario 7
    public async Task ResourceFilter_StillWorks()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var result = await GetAsync("?resource=Production%20Database");

        Assert.Single(result.Items);
    }

    [Fact] // scenario 8
    public async Task EventTypeFilter_StillWorks()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var result = await GetAsync("?eventType=security.auth.login");

        Assert.Single(result.Items);
    }

    [Fact] // scenario 9
    public async Task OutcomeFilter_Works()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var result = await GetAsync("?outcome=DENIED");

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, i => Assert.Equal("DENIED", i.Outcome));
    }

    // ------------------------------------------------------------- date range

    [Fact] // scenario 10
    public async Task FromFilter_Works()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var result = await GetAsync("?from=2026-10-15T00:00:00.000Z");

        Assert.Equal(3, result.TotalCount);
    }

    [Fact] // scenario 11
    public async Task ToFilter_Works()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var result = await GetAsync("?to=2026-10-01T23:59:59.999Z");

        Assert.Single(result.Items);
    }

    [Fact] // scenario 12
    public async Task FromAndToRange_Works()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var result = await GetAsync("?from=2026-10-01T00:00:00.000Z&to=2026-10-31T23:59:59.999Z");

        Assert.Equal(3, result.TotalCount);
        Assert.DoesNotContain(result.Items, i => i.OccurredAt == November5);
    }

    [Fact]
    public async Task ToFilter_IncludesEventsAtTheVeryEndOfTheSelectedDay()
    {
        await _fixture.ResetAsync();
        await SeedAsync(AuditLogEntryBuilder.Create("Alice Fernando", Resource, occurredAt: October31Late));

        var result = await GetAsync("?to=2026-10-31T23:59:59.999Z");

        Assert.Single(result.Items);
    }

    [Fact] // scenario 13
    public async Task FromAfterTo_Returns400()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync(
            "/api/audit/logs?from=2026-10-31T00:00:00.000Z&to=2026-10-01T00:00:00.000Z");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("from", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FromEqualTo_IsAccepted()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var result = await GetAsync("?from=2026-10-01T09:00:00.000Z&to=2026-10-01T09:00:00.000Z");

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task MalformedDate_Returns400Not500()
    {
        await _fixture.ResetAsync();
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/audit/logs?from=not-a-date");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------------------------ composition

    [Fact] // scenario 14 - search + outcome
    public async Task SearchCombinedWithOutcome_Works()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var result = await GetAsync("?search=alice&outcome=DENIED");

        var item = Assert.Single(result.Items);
        Assert.Equal("Development Laptop", item.Resource);
    }

    [Fact] // scenario 15
    public async Task Filters_ComposeWithAndSemantics()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var result = await GetAsync(
            "?search=laptop&eventType=security.access.denied&outcome=DENIED&from=2026-11-01T00:00:00.000Z");

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task SearchDoesNotOverrideOtherFilters()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        // "alice" matches two rows, but only one of them is a SUCCESS event.
        var loose = await GetAsync("?search=alice");
        var tightened = await GetAsync("?search=alice&outcome=SUCCESS");

        Assert.Equal(2, loose.TotalCount);
        Assert.Single(tightened.Items);
    }

    // -------------------------------------------------- pagination & contract

    [Fact] // scenario 16
    public async Task TotalCount_IsBasedOnTheFilteredResultBeforePagination()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var result = await GetAsync("?outcome=DENIED&page=1&pageSize=1");

        Assert.Equal(2, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal(1, result.PageSize);
    }

    [Fact] // scenario 17
    public async Task Page1AndPage2_DoNotOverlap()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var page1 = await GetAsync("?page=1&pageSize=2");
        var page2 = await GetAsync("?page=2&pageSize=2");

        Assert.Empty(page1.Items.Select(i => i.Id).Intersect(page2.Items.Select(i => i.Id)));
        Assert.Equal(4, page1.Items.Count() + page2.Items.Count());
    }

    [Fact] // scenario 18 - identical timestamps must not reshuffle between pages
    public async Task Ordering_IsStableAcrossPages_WhenOccurredAtIsIdentical()
    {
        await _fixture.ResetAsync();
        var shared = new DateTimeOffset(2026, 12, 25, 8, 0, 0, TimeSpan.Zero);
        await SeedAsync(Enumerable.Range(1, 6)
            .Select(i => AuditLogEntryBuilder.Create($"Actor{i}", Resource, occurredAt: shared))
            .ToArray());

        var page1 = await GetAsync("?page=1&pageSize=3");
        var page2 = await GetAsync("?page=2&pageSize=3");

        Assert.Equal(6, page1.TotalCount);
        Assert.Empty(page1.Items.Select(i => i.Id).Intersect(page2.Items.Select(i => i.Id)));

        // Repeating the very same query must return the very same order.
        var repeat = await GetAsync("?page=1&pageSize=3");
        Assert.Equal(page1.Items.Select(i => i.Id), repeat.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task ResultsAreOrderedByOccurredAtDescending()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var result = await GetAsync(string.Empty);
        var timestamps = result.Items.Select(i => i.OccurredAt).ToList();

        Assert.Equal(timestamps.OrderByDescending(t => t).ToList(), timestamps);
    }

    // ------------------------------------------------------- response contract

    [Fact] // scenario 20 - the existing API shape is unchanged
    public async Task Response_KeepsTheExistingContract()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/audit/logs?page=1&pageSize=20");
        var raw = await response.Content.ReadAsStringAsync();
        var document = JsonDocument.Parse(raw).RootElement;

        Assert.Equal(4, document.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, document.GetProperty("page").GetInt32());
        Assert.Equal(20, document.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, document.GetProperty("totalPages").GetInt32());

        var item = document.GetProperty("items")[0];
        Assert.True(item.TryGetProperty("id", out _));
        Assert.True(item.TryGetProperty("eventId", out _));
        Assert.True(item.TryGetProperty("eventType", out _));
        Assert.True(item.TryGetProperty("occurredAt", out _));
        Assert.True(item.TryGetProperty("actor", out _));
        Assert.True(item.TryGetProperty("action", out _));
        Assert.True(item.TryGetProperty("outcome", out _));
        Assert.True(item.TryGetProperty("resource", out _));
    }

    [Fact]
    public async Task LegacyQueryStringWithoutNewParameters_StillWorks()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        // The pre-BIS-403 call shape must remain valid. Alice has two rows, but
        // only one of them is a `granted` event, so the two filters AND together.
        var result = await GetAsync(
            "?user=Alice%20Fernando&eventType=security.access.granted&page=1&pageSize=20");

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Production Database", result.Items.Single().Resource);
    }

    [Fact]
    public async Task LegacyUserFilterAlone_StillReturnsEveryMatch()
    {
        await _fixture.ResetAsync();
        await SeedDefaultDatasetAsync();

        var result = await GetAsync("?user=Alice%20Fernando&page=1&pageSize=20");

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task EmptyDatabase_ReturnsAnEmptyPageRatherThanAnError()
    {
        await _fixture.ResetAsync();

        var result = await GetAsync(string.Empty);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }
}