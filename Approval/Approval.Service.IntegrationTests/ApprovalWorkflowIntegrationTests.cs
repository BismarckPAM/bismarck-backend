using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Approval.Service.DTOs;
using Approval.Service.Models;

namespace Approval.Service.IntegrationTests;

public sealed class ApprovalWorkflowIntegrationTests
    : IClassFixture<ApprovalApiFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ApprovalApiFixture fixture;

    public ApprovalWorkflowIntegrationTests(ApprovalApiFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task RequestLifecycle_CreateApproveAndReject()
    {
        using var requester = CreateClient("requester-1", "Developer");
        var createResponse = await requester.PostAsJsonAsync(
            "/api/approval/requests",
            new CreateApprovalRequestRequest
            {
                ResourceId = "resource-1",
                RequestedLevel = 4,
                Reason = "Production incident",
                DurationMinutes = 120
            },
            JsonOptions);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ApprovalRequestResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal(ApprovalStatus.PENDING, created.Status);
        Assert.Equal("requester-1", created.RequesterUserId);

        using var approver = CreateClient("approver-1", "Admin");
        var pendingResponse = await approver.GetAsync("/api/approval/requests");
        Assert.Equal(HttpStatusCode.OK, pendingResponse.StatusCode);
        var pending = await pendingResponse.Content.ReadFromJsonAsync<List<ApprovalRequestResponse>>(JsonOptions);
        Assert.NotNull(pending);
        Assert.Contains(pending, item => item.Id == created.Id);

        var approveResponse = await approver.PostAsync(
            $"/api/approval/requests/{created.Id}/approve",
            content: null);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var approved = await approveResponse.Content.ReadFromJsonAsync<ApprovalRequestResponse>(JsonOptions);
        Assert.NotNull(approved);
        Assert.Equal(ApprovalStatus.APPROVED, approved.Status);
        Assert.Equal("approver-1", approved.ReviewedByUserId);
    }

    [Fact]
    public async Task RejectRequiresReasonAndCannotBeRepeated()
    {
        using var approver = CreateClient("approver-2", "Admin");
        var createResponse = await approver.PostAsJsonAsync(
            "/api/approval/requests",
            new CreateApprovalRequestRequest
            {
                ResourceId = "resource-2",
                RequestedLevel = 2,
                Reason = "Temporary access",
                DurationMinutes = 30
            },
            JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<ApprovalRequestResponse>(JsonOptions);
        Assert.NotNull(created);

        var missingReasonResponse = await approver.PostAsJsonAsync(
            $"/api/approval/requests/{created.Id}/reject",
            new RejectApprovalRequest(),
            JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, missingReasonResponse.StatusCode);

        var rejectResponse = await approver.PostAsJsonAsync(
            $"/api/approval/requests/{created.Id}/reject",
            new RejectApprovalRequest { Reason = "Access is too broad" },
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);

        var secondActionResponse = await approver.PostAsync(
            $"/api/approval/requests/{created.Id}/approve",
            content: null);
        Assert.Equal(HttpStatusCode.Conflict, secondActionResponse.StatusCode);
    }

    [Fact]
    public async Task NonApproverCannotListOrActionRequests()
    {
        using var client = CreateClient("developer-1", "Developer");

        var listResponse = await client.GetAsync("/api/approval/requests");
        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);
    }

    [Fact]
    public async Task MissingOrInvalidTokenReturnsUnauthorized()
    {
        using var client = fixture.CreateClient();

        var missingTokenResponse = await client.GetAsync("/api/approval/requests");
        Assert.Equal(HttpStatusCode.Unauthorized, missingTokenResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "invalid.token.value");
        var invalidTokenResponse = await client.GetAsync("/api/approval/requests");
        Assert.Equal(HttpStatusCode.Unauthorized, invalidTokenResponse.StatusCode);
    }

    private HttpClient CreateClient(string userId, string role)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApprovalApiFixture.GenerateValidJwtToken(userId, role));
        return client;
    }
}
