using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Analytics.Service.DTOs;
using Analytics.Service.Models;
using Messaging;
using Microsoft.EntityFrameworkCore;

namespace Analytics.Service.IntegrationTests;

/// <summary>
/// End-to-end coverage of the three BIS-402 endpoints against a real host and a
/// real PostgreSQL database: status codes, JSON contract, date validation and
/// authentication.
/// </summary>
public sealed class AnalyticsEndpointTests : IClassFixture<AnalyticsApiFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static readonly DateTimeOffset October1 = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset October15 = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset November5 = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    private readonly AnalyticsApiFixture _fixture;

    public AnalyticsEndpointTests(AnalyticsApiFixture fixture)
    {
        _fixture = fixture;
    }

    internal Task SeedAsync(params AnalyticsEvent[] events) =>
        _fixture.WithDbContextAsync(async context =>
        {
            context.AnalyticsEvents.AddRange(events);
            await context.SaveChangesAsync();
        });

    // =====================================================================
    // AUTH (scenarios 35-36)
    // =====================================================================

    [Theory] // 35
    [InlineData("/api/analytics/summary")]
    [InlineData("/api/analytics/top-resources")]
    [InlineData("/api/analytics/denial-reasons")]
    public async Task Endpoints_WithoutToken_Return401(string path)
    {
        using var client = _fixture.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/analytics/summary")]
    [InlineData("/api/analytics/top-resources")]
    [InlineData("/api/analytics/denial-reasons")]
    public async Task Endpoints_WithInvalidToken_Return401(string path)
    {
        using var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "not.a.jwt");

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory] // 36
    [InlineData("/api/analytics/summary")]
    [InlineData("/api/analytics/top-resources")]
    [InlineData("/api/analytics/denial-reasons")]
    public async Task Endpoints_WithValidToken_AreAccessible(string path)
    {
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    // =====================================================================
    // DATE VALIDATION (scenarios 1-7)
    // =====================================================================

    [Fact] // 1
    public async Task Summary_NoDates_Returns200()
    {
        await _fixture.ResetAsync();
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/analytics/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact] // 2
    public async Task Summary_StartDateOnly_FiltersCorrectly()
    {
        await _fixture.ResetAsync();
        await SeedAsync(
            IntegrationEventBuilder.ApprovalRequested(October1),
            IntegrationEventBuilder.ApprovalRequested(October15),
            IntegrationEventBuilder.ApprovalRequested(November5));

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync(
            "/api/analytics/summary?startDate=2026-10-10T00:00:00Z");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AnalyticsSummaryResponse>(JsonOptions);

        Assert.NotNull(body);
        Assert.Equal(2, body!.Totals.Requests);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero), body.StartDate);
    }

    [Fact] // 3
    public async Task Summary_EndDateOnly_FiltersCorrectly()
    {
        await _fixture.ResetAsync();
        await SeedAsync(
            IntegrationEventBuilder.ApprovalRequested(October1),
            IntegrationEventBuilder.ApprovalRequested(October15),
            IntegrationEventBuilder.ApprovalRequested(November5));

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync(
            "/api/analytics/summary?endDate=2026-10-20T00:00:00Z");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AnalyticsSummaryResponse>(JsonOptions);

        Assert.Equal(2, body!.Totals.Requests);
    }

    [Fact] // 4
    public async Task Summary_StartAndEndDate_FiltersCorrectly()
    {
        await _fixture.ResetAsync();
        await SeedAsync(
            IntegrationEventBuilder.ApprovalRequested(October1),
            IntegrationEventBuilder.ApprovalRequested(October15),
            IntegrationEventBuilder.ApprovalRequested(November5));

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync(
            "/api/analytics/summary?startDate=2026-10-01T00:00:00Z&endDate=2026-10-20T00:00:00Z");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AnalyticsSummaryResponse>(JsonOptions);

        Assert.Equal(2, body!.Totals.Requests);
        Assert.Equal(2, body.Trend.Count);
    }

    [Theory] // 5 + DoD-3
    [InlineData("/api/analytics/summary")]
    [InlineData("/api/analytics/top-resources")]
    [InlineData("/api/analytics/denial-reasons")]
    public async Task Endpoints_StartAfterEnd_Return400(string path)
    {
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync(
            $"{path}?startDate=2026-10-31T00:00:00Z&endDate=2026-10-01T00:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory] // 6 + DoD-3
    [InlineData("/api/analytics/summary")]
    [InlineData("/api/analytics/top-resources")]
    [InlineData("/api/analytics/denial-reasons")]
    public async Task Endpoints_MalformedStartDate_Return400(string path)
    {
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync($"{path}?startDate=not-a-real-date");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory] // 7 + DoD-3
    [InlineData("/api/analytics/summary")]
    [InlineData("/api/analytics/top-resources")]
    [InlineData("/api/analytics/denial-reasons")]
    public async Task Endpoints_MalformedEndDate_Return400(string path)
    {
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync($"{path}?endDate=31-31-2026");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Summary_InvalidRange_ReturnsValidationProblemDetails()
    {
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync(
            "/api/analytics/summary?startDate=2026-12-01T00:00:00Z&endDate=2026-01-01T00:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("startDate", out _));
    }
    // =====================================================================
    // METRIC CORRECTNESS OVER REAL POSTGRESQL
    // These duplicate the unit assertions on purpose: they prove the numbers
    // are identical when the LINQ is executed by PostgreSQL rather than SQLite,
    // which is the DoD-4 "verifiable against Audit Logs" requirement.
    // =====================================================================

    [Fact] // 8 + 9-16
    public async Task Summary_AppliesTheCanonicalMetricMapping()
    {
        await _fixture.ResetAsync();
        await SeedAsync(
            // Requests
            IntegrationEventBuilder.ApprovalRequested(October1),
            IntegrationEventBuilder.ApprovalRequested(October1),
            // Approvals
            IntegrationEventBuilder.ApprovalGranted(October1),
            // Denials: both halves
            IntegrationEventBuilder.AccessDenied(October1, "INSUFFICIENT_ROLE_PERMISSIONS"),
            IntegrationEventBuilder.ApprovalRejected(October1, "Not justified"),
            // Revocations: expiry + admin JIT manual revoke
            IntegrationEventBuilder.PermissionRevoked(October1),
            IntegrationEventBuilder.PermissionRevoked(October1, KafkaTopics.JitRevoked),
            // MUST NOT be counted anywhere
            IntegrationEventBuilder.AccessRequested(October1),
            IntegrationEventBuilder.AccessGranted(October1));

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/analytics/summary");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AnalyticsSummaryResponse>(JsonOptions);

        Assert.Equal(2, body!.Totals.Requests);    // ApprovalRequested only
        Assert.Equal(1, body.Totals.Approvals);   // ApprovalGranted only
        Assert.Equal(2, body.Totals.Denials);     // AccessDenied + ApprovalRejected
        Assert.Equal(2, body.Totals.Revocations); // PermissionRevoked + jit-revoked

        // Totals must equal the sum of the trend.
        Assert.Equal(body.Trend.Sum(p => p.Requests), body.Totals.Requests);
        Assert.Equal(body.Trend.Sum(p => p.Approvals), body.Totals.Approvals);
        Assert.Equal(body.Trend.Sum(p => p.Denials), body.Totals.Denials);
        Assert.Equal(body.Trend.Sum(p => p.Revocations), body.Totals.Revocations);
    }

    [Fact] // 17 + 18
    public async Task Summary_TrendGroupsByUtcDayAndRespectsRange()
    {
        await _fixture.ResetAsync();
        await SeedAsync(
            IntegrationEventBuilder.ApprovalRequested(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)),
            IntegrationEventBuilder.ApprovalRequested(new DateTimeOffset(2026, 10, 1, 23, 59, 59, TimeSpan.Zero)),
            IntegrationEventBuilder.ApprovalRequested(new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero)),
            // Outside the requested window.
            IntegrationEventBuilder.ApprovalRequested(November5));

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync(
            "/api/analytics/summary?startDate=2026-10-01T00:00:00Z&endDate=2026-10-31T00:00:00Z");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AnalyticsSummaryResponse>(JsonOptions);

        Assert.Equal(2, body!.Trend.Count);
        Assert.Equal(new DateOnly(2026, 10, 1), body.Trend[0].Date);
        Assert.Equal(2, body.Trend[0].Requests);
        Assert.Equal(new DateOnly(2026, 10, 2), body.Trend[1].Date);
        Assert.Equal(1, body.Trend[1].Requests);
        Assert.Equal(3, body.Totals.Requests);
    }
    [Fact] // 19-23
    public async Task TopResources_RanksByRequestCountWithCorrectPercentages()
    {
        await _fixture.ResetAsync();
        for (var i = 0; i < 5; i++)
        {
            await SeedAsync(IntegrationEventBuilder.ApprovalRequested(October1, "Production Database", "res-1"));
        }

        for (var i = 0; i < 4; i++)
        {
            await SeedAsync(IntegrationEventBuilder.ApprovalRequested(October1, "Billing API", "res-2"));
        }

        // No ResourceName -> must fall back to the identifier.
        await SeedAsync(IntegrationEventBuilder.ApprovalRequested(October1, resourceName: null, resource: "res-3"));

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/analytics/top-resources");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<TopResourcesResponse>(JsonOptions);

        Assert.Equal(10, body!.TotalRequests);
        Assert.Equal(3, body.Items.Count);

        Assert.Equal(1, body.Items[0].Rank);
        Assert.Equal("Production Database", body.Items[0].ResourceName);
        Assert.Equal("res-1", body.Items[0].ResourceId);
        Assert.Equal(5, body.Items[0].RequestCount);
        Assert.Equal(50.00m, body.Items[0].Percentage);

        Assert.Equal(2, body.Items[1].Rank);
        Assert.Equal(4, body.Items[1].RequestCount);
        Assert.Equal(40.00m, body.Items[1].Percentage);

        Assert.Equal("res-3", body.Items[2].ResourceName);
        Assert.Equal(10.00m, body.Items[2].Percentage);
    }

    [Fact] // 25-30
    public async Task DenialReasons_DistributesAcrossBothDenialSources()
    {
        await _fixture.ResetAsync();
        for (var i = 0; i < 5; i++)
        {
            await SeedAsync(IntegrationEventBuilder.AccessDenied(October1, "INSUFFICIENT_ROLE_PERMISSIONS"));
        }

        for (var i = 0; i < 4; i++)
        {
            await SeedAsync(IntegrationEventBuilder.ApprovalRejected(October1, "Not justified"));
        }

        // Missing reason -> UNKNOWN bucket.
        await SeedAsync(IntegrationEventBuilder.AccessDenied(October1, reason: null));

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/analytics/denial-reasons");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<DenialReasonsResponse>(JsonOptions);

        Assert.Equal(10, body!.TotalDenials);
        Assert.Equal(3, body.Items.Count);

        Assert.Equal("INSUFFICIENT_ROLE_PERMISSIONS", body.Items[0].Reason);
        Assert.Equal(5, body.Items[0].Count);
        Assert.Equal(50.00m, body.Items[0].Percentage);

        Assert.Equal("Not justified", body.Items[1].Reason);
        Assert.Equal(40.00m, body.Items[1].Percentage);

        Assert.Equal(AnalyticsMetricMap.UnknownDenialReason, body.Items[2].Reason);
        Assert.Equal(10.00m, body.Items[2].Percentage);
    }
    [Fact] // 8, 24, 30 - zero-data responses
    public async Task AllEndpoints_EmptyDatabase_ReturnZerosNotErrors()
    {
        await _fixture.ResetAsync();
        using var client = _fixture.CreateAuthenticatedClient();

        var summary = await client.GetAsync("/api/analytics/summary");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        var summaryBody = await summary.Content.ReadFromJsonAsync<AnalyticsSummaryResponse>(JsonOptions);
        Assert.Equal(0, summaryBody!.Totals.Requests);
        Assert.Empty(summaryBody.Trend);

        var top = await client.GetAsync("/api/analytics/top-resources");
        Assert.Equal(HttpStatusCode.OK, top.StatusCode);
        var topBody = await top.Content.ReadFromJsonAsync<TopResourcesResponse>(JsonOptions);
        Assert.Equal(0, topBody!.TotalRequests);
        Assert.Empty(topBody.Items);

        var reasons = await client.GetAsync("/api/analytics/denial-reasons");
        Assert.Equal(HttpStatusCode.OK, reasons.StatusCode);
        var reasonsBody = await reasons.Content.ReadFromJsonAsync<DenialReasonsResponse>(JsonOptions);
        Assert.Equal(0, reasonsBody!.TotalDenials);
        Assert.Empty(reasonsBody.Items);
    }

    [Fact]
    public async Task Summary_RangeExcludingAllEvents_ReturnsZeroResults()
    {
        await _fixture.ResetAsync();
        await SeedAsync(IntegrationEventBuilder.ApprovalRequested(October1));

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync(
            "/api/analytics/summary?startDate=2026-01-01T00:00:00Z&endDate=2026-01-31T00:00:00Z");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AnalyticsSummaryResponse>(JsonOptions);

        Assert.Equal(0, body!.Totals.Requests);
        Assert.Empty(body.Trend);
    }

    [Fact] // 31 - the unique EventId index is enforced by PostgreSQL
    public async Task Database_RejectsDuplicateEventId()
    {
        await _fixture.ResetAsync();
        var eventId = Guid.NewGuid();

        await SeedAsync(IntegrationEventBuilder.ApprovalRequested(October1, eventId: eventId));

        // Re-inserting the same EventId must be rejected by the unique index.
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => SeedAsync(
            IntegrationEventBuilder.ApprovalRequested(October1, eventId: eventId)));

        await _fixture.WithDbContextAsync(async context =>
        {
            var stored = await context.AnalyticsEvents.CountAsync(e => e.EventId == eventId);
            Assert.Equal(1, stored);
        });
    }

    [Fact] // 34
    public async Task Database_PersistsSourceTopicAndJsonbMetadata()
    {
        await _fixture.ResetAsync();
        await SeedAsync(IntegrationEventBuilder.ApprovalRejected(October1, "Business justification missing"));

        await _fixture.WithDbContextAsync(async context =>
        {
            var stored = await context.AnalyticsEvents.AsNoTracking().SingleAsync();

            Assert.Equal(KafkaTopics.ApprovalRejected, stored.SourceTopic);
            Assert.Equal(AnalyticsMetricMap.ApprovalRejectedEventType, stored.EventType);
            Assert.Equal("Business justification missing", stored.DenialReason);
            Assert.Contains("RejectionReason", stored.Metadata);
            Assert.Equal(new DateOnly(2026, 10, 1), stored.OccurredAtDate);
        });
    }

    [Fact] // Swagger must be reachable (DoD-6)
    public async Task Swagger_IsServedInTestingEnvironment()
    {
        using var client = _fixture.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
