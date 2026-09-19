using System.Security.Claims;
using Approval.Service.Data;
using Approval.Service.DTOs;
using Approval.Service.Models;
using Approval.Service.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Approval.Service.Tests;

public sealed class ApprovalServiceTests
{
    [Fact]
    public async Task CreateAsync_UsesAuthenticatedUserAndCreatesPendingRequest()
    {
        await using var fixture = CreateFixture("requester-1", "Developer");
        var service = fixture.CreateService();

        var result = await service.CreateAsync(new CreateApprovalRequestRequest
        {
            ResourceId = "resource-1",
            RequestedLevel = 4,
            Reason = "Incident investigation",
            DurationMinutes = 120
        });

        Assert.Equal("requester-1", result.RequesterUserId);
        Assert.Equal(ApprovalStatus.PENDING, result.Status);
        Assert.Equal("resource-1", result.ResourceId);
        Assert.Equal(4, result.RequestedLevel);
        Assert.Equal(120, result.DurationMinutes);
    }

    [Fact]
    public async Task GetPendingAsync_ExcludesActionedRequests()
    {
        await using var fixture = CreateFixture("approver-1", "Admin");
        fixture.AddRequest(ApprovalStatus.PENDING, "pending");
        fixture.AddRequest(ApprovalStatus.APPROVED, "approved");
        fixture.AddRequest(ApprovalStatus.REJECTED, "rejected");
        var service = fixture.CreateService();

        var result = await service.GetPendingAsync();

        var request = Assert.Single(result);
        Assert.Equal("pending", request.Reason);
    }

    [Fact]
    public async Task IsApprover_UsesConfiguredRole()
    {
        await using var adminFixture = CreateFixture("admin-1", "Admin");
        Assert.True(adminFixture.CreateService().IsApprover());

        await using var developerFixture = CreateFixture("developer-1", "Developer");
        Assert.False(developerFixture.CreateService().IsApprover());
    }

    [Fact]
    public async Task ApproveAsync_UpdatesRequestAndPublishesEvent()
    {
        await using var fixture = CreateFixture("approver-1", "Admin");
        var request = fixture.AddRequest(ApprovalStatus.PENDING, "approve me");
        var publisher = new RecordingPublisher();
        var service = fixture.CreateService(publisher);

        var result = await service.ApproveAsync(request.Id);

        Assert.Equal(ApprovalStatus.APPROVED, result.Status);
        Assert.Equal("approver-1", result.ReviewedByUserId);
        var publishedEvent = Assert.Single(publisher.Events);
        Assert.Equal("ApprovalGranted", publishedEvent.EventType);
        Assert.Equal(request.Id, publishedEvent.EntityId);
    }

    [Fact]
    public async Task RejectAsync_StoresReasonAndReviewer()
    {
        await using var fixture = CreateFixture("approver-1", "Admin");
        var request = fixture.AddRequest(ApprovalStatus.PENDING, "reject me");
        var service = fixture.CreateService();

        var result = await service.RejectAsync(request.Id, "Insufficient justification");

        Assert.Equal(ApprovalStatus.REJECTED, result.Status);
        Assert.Equal("Insufficient justification", result.RejectionReason);
        Assert.Equal("approver-1", result.ReviewedByUserId);
    }

    [Fact]
    public async Task ApproveAsync_ThrowsConflictForAlreadyActionedRequest()
    {
        await using var fixture = CreateFixture("approver-1", "Admin");
        var request = fixture.AddRequest(ApprovalStatus.APPROVED, "already approved");
        var service = fixture.CreateService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApproveAsync(request.Id));

        Assert.Contains("already been actioned", exception.Message);
    }

    [Fact]
    public async Task RejectAsync_ThrowsNotFoundForUnknownRequest()
    {
        await using var fixture = CreateFixture("approver-1", "Admin");
        var service = fixture.CreateService();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.RejectAsync(Guid.NewGuid(), "Not applicable"));
    }

    private static TestFixture CreateFixture(string userId, string role)
        => new(userId, role);

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ApprovalDbContext context;
        private readonly HttpContextAccessor httpContextAccessor;
        private readonly IConfiguration configuration;

        public TestFixture(string userId, string role)
        {
            connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite(connection)
                .Options;
            context = new ApprovalDbContext(options);
            context.Database.EnsureCreated();
            httpContextAccessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [
                            new Claim(ClaimTypes.NameIdentifier, userId),
                            new Claim(ClaimTypes.Role, role)
                        ],
                        "Test"))
                }
            };
            configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Approval:ApproverRoles:0"] = "Admin"
                })
                .Build();
        }

        public ApprovalRequest AddRequest(ApprovalStatus status, string reason)
        {
            var request = new ApprovalRequest
            {
                RequesterUserId = "requester-1",
                ResourceId = "resource-1",
                RequestedLevel = 3,
                Reason = reason,
                DurationMinutes = 60,
                Status = status
            };
            context.ApprovalRequests.Add(request);
            context.SaveChanges();
            return request;
        }

        public ApprovalService CreateService(IDomainEventPublisher? publisher = null)
            => new(context, httpContextAccessor, configuration, publisher ?? new RecordingPublisher());

        public async ValueTask DisposeAsync()
        {
            await context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class RecordingPublisher : IDomainEventPublisher
    {
        public List<DomainEventMessage> Events { get; } = [];

        public Task PublishAsync(
            DomainEventMessage message,
            CancellationToken cancellationToken = default)
        {
            Events.Add(message);
            return Task.CompletedTask;
        }
    }
}
