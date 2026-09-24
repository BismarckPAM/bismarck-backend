using System.Security.Claims;
using AuthorizationService.Clients;
using AuthorizationService.Controllers;
using AuthorizationService.Data;
using AuthorizationService.Models;
using AuthorizationService.Services;
using Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AuthorizationService.Tests;

public sealed class TemporaryPermissionExpirationWorkerTests
{
    [Fact]
    public async Task ExpirePermissionsOnceAsync_ExpiresOverduePermission()
    {
        var now = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        await using var fixture = CreateFixture(now);
        var permission = fixture.AddPermission(now.AddMinutes(-1));
        var publisher = new Mock<IAuthorizationEventPublisher>();
        var worker = fixture.CreateWorker(new TestClock(now), publisher);

        await worker.ExpirePermissionsOnceAsync();

        var saved = await fixture.Context.TemporaryPermissions
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(TemporaryPermissionStatus.EXPIRED, saved.Status);
        Assert.Equal(now, saved.RevokedAt);
    }

    [Fact]
    public async Task ExpirePermissionsOnceAsync_PublishesPermissionRevoked()
    {
        var now = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        await using var fixture = CreateFixture(now);
        var permission = fixture.AddPermission(now);
        var publisher = new Mock<IAuthorizationEventPublisher>();
        var worker = fixture.CreateWorker(new TestClock(now), publisher);

        await worker.ExpirePermissionsOnceAsync();

        publisher.Verify(item => item.PublishAsync(
            KafkaTopics.PermissionRevoked,
            It.Is<SecurityEvent<object>>(message =>
                message.EventType == SecurityEventTypes.PermissionRevoked
                && message.Action == "EXPIRE_PERMISSION"
                && message.Outcome == "SUCCESS"
                && message.Resource == permission.ResourceId.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static WorkerFixture CreateFixture(DateTimeOffset now) => new(now);

    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        public AuthorizationDbContext Context { get; }

        public WorkerFixture(DateTimeOffset now)
        {
            var services = new ServiceCollection();
            var databaseName = Guid.NewGuid().ToString();
            services.AddDbContext<AuthorizationDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
            provider = services.BuildServiceProvider();
            Context = provider.GetRequiredService<AuthorizationDbContext>();
            Context.Database.EnsureCreated();
        }

        public TemporaryPermission AddPermission(DateTimeOffset expiresAt)
        {
            var permission = new TemporaryPermission
            {
                ApprovalId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                ResourceId = Guid.NewGuid(),
                RequestedLevel = 4,
                GrantedAt = expiresAt.AddMinutes(-30),
                ExpiresAt = expiresAt,
                Status = TemporaryPermissionStatus.ACTIVE
            };
            Context.TemporaryPermissions.Add(permission);
            Context.SaveChanges();
            return permission;
        }

        public TemporaryPermissionExpirationWorker CreateWorker(
            ISystemClock clock,
            Mock<IAuthorizationEventPublisher> publisher)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ExpirationWorker:IntervalSeconds"] = "60"
                })
                .Build();
            return new TemporaryPermissionExpirationWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                clock,
                configuration,
                NullLogger<TemporaryPermissionExpirationWorker>.Instance,
                publisher.Object);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await provider.DisposeAsync();
        }
    }

    private sealed class TestClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow => now;
    }
}

public sealed class ManualRevokeTests
{
    [Fact]
    public async Task RevokePermission_NonAdmin_ReturnsForbidden()
    {
        await using var context = CreateContext();
        var controller = CreateController(context, "Developer");

        var result = await controller.RevokePermission(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task RevokePermission_ExpiredPermission_ReturnsConflict()
    {
        var now = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        await using var context = CreateContext();
        var permission = new TemporaryPermission
        {
            ApprovalId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ResourceId = Guid.NewGuid(),
            RequestedLevel = 3,
            GrantedAt = now.AddMinutes(-60),
            ExpiresAt = now.AddMinutes(-1),
            Status = TemporaryPermissionStatus.EXPIRED
        };
        context.TemporaryPermissions.Add(permission);
        await context.SaveChangesAsync();
        var controller = CreateController(context, "Admin", now);

        var result = await controller.RevokePermission(permission.Id, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(409, conflict.StatusCode);
    }

    private static AuthorizationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AuthorizationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AuthorizationDbContext(options);
    }

    private static AuthorizationController CreateController(
        AuthorizationDbContext context,
        string role,
        DateTimeOffset? now = null)
    {
        var userId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Role, role)
                ],
                "Test"))
        };
        var controller = new AuthorizationController(
            Mock.Of<IIdentityServiceClient>(),
            Mock.Of<IResourceServiceClient>(),
            Mock.Of<IPolicyDecisionEngine>(),
            Mock.Of<IAuthorizationEventPublisher>(),
            context,
            new TestClock(now ?? DateTimeOffset.UtcNow));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        return controller;
    }

    private sealed class TestClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
