using System.ComponentModel.DataAnnotations;
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
    public static IEnumerable<object[]> ValidCreateRequests =>
        Enumerable.Range(1, 101)
            .Select(caseNumber => new object[]
            {
                $"resource-{caseNumber}",
                (caseNumber % 5) + 1,
                $"Business justification for test case {caseNumber}",
                (caseNumber % 1440) + 1
            });

    [Theory]
    [MemberData(nameof(ValidCreateRequests))]
    public async Task CreateAsync_WithValidRequestData_CreatesPendingRequest(
        string resourceId,
        int requestedLevel,
        string reason,
        int durationMinutes)
    {
        await using var fixture = CreateFixture("requester-parameterized", "Developer");
        var service = fixture.CreateService();

        var result = await service.CreateAsync(new CreateApprovalRequestRequest
        {
            ResourceId = resourceId,
            RequestedLevel = requestedLevel,
            Reason = reason,
            DurationMinutes = durationMinutes
        });

        Assert.Equal(ApprovalStatus.PENDING, result.Status);
        Assert.Equal(resourceId, result.ResourceId);
        Assert.Equal(requestedLevel, result.RequestedLevel);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(durationMinutes, result.DurationMinutes);
    }

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
    public async Task CreateAsync_WithoutUserId_ThrowsUnauthorized()
    {
        await using var fixture = CreateFixture("requester-1", "Developer", authenticated: false);
        var service = fixture.CreateService();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync(
            new CreateApprovalRequestRequest
            {
                ResourceId = "resource-1",
                RequestedLevel = 1,
                Reason = "A valid reason",
                DurationMinutes = 30
            }));
    }

    [Fact]
    public void CreateRequest_ValidationRejectsMissingAndOutOfRangeValues()
    {
        var request = new CreateApprovalRequestRequest
        {
            RequestedLevel = 6,
            DurationMinutes = 0
        };
        var errors = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            errors,
            validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(request.ResourceId)));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(request.Reason)));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(request.RequestedLevel)));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(request.DurationMinutes)));
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
    public async Task GetByIdAsync_WhenRequestDoesNotExist_ThrowsNotFound()
    {
        await using var fixture = CreateFixture("approver-1", "Admin");
        var service = fixture.CreateService();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.GetByIdAsync(Guid.NewGuid()));
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
    public async Task RejectAsync_ThrowsConflictForAlreadyActionedRequest()
    {
        await using var fixture = CreateFixture("approver-1", "Admin");
        var request = fixture.AddRequest(ApprovalStatus.REJECTED, "already rejected");
        var service = fixture.CreateService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RejectAsync(request.Id, "Try again"));

        Assert.Contains("already been actioned", exception.Message);
    }

    [Fact]
    public async Task ApproveAsync_WhenRequestDoesNotExist_ThrowsNotFoundAndPublishesNothing()
    {
        await using var fixture = CreateFixture("approver-1", "Admin");
        var publisher = new RecordingPublisher();
        var service = fixture.CreateService(publisher);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.ApproveAsync(Guid.NewGuid()));

        Assert.Empty(publisher.Events);
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

    private static TestFixture CreateFixture(
        string userId,
        string role,
        bool authenticated = true)
        => new(userId, role, authenticated);

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ApprovalDbContext context;
        private readonly HttpContextAccessor httpContextAccessor;
        private readonly IConfiguration configuration;

        public TestFixture(string userId, string role, bool authenticated)
        {
            connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<ApprovalDbContext>()
                .UseSqlite(connection)
                .Options;
            context = new ApprovalDbContext(options);
            context.Database.EnsureCreated();
            var claims = authenticated
                ? new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, userId),
                        new Claim(ClaimTypes.Role, role)
                    ],
                    "Test"))
                : new ClaimsPrincipal(new ClaimsIdentity());
            httpContextAccessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = claims
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
        public List<RecordedEvent> Events { get; } = [];

        public Task PublishAsync<T>(
            DomainEventMessage<T> message,
            CancellationToken cancellationToken = default)
        {
            Events.Add(new RecordedEvent(message.EventType, message.EntityId));
            return Task.CompletedTask;
        }

        public sealed record RecordedEvent(string EventType, Guid EntityId);
    }
}
