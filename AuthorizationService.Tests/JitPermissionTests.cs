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

    /// <summary>
    /// The brokered terminal must be torn down at expiry, not merely marked as
    /// no longer authorised. This is what makes the JIT countdown an actual
    /// control rather than a record.
    /// </summary>
    [Fact]
    public async Task ExpirePermissionsOnceAsync_ClosesLiveBrokeredTerminal()
    {
        var now = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        await using var fixture = CreateFixture(now);
        var permission = fixture.AddPermission(now);

        var terminalBroker = new Mock<IJitTerminalBroker>();
        var worker = fixture.CreateWorker(
            new TestClock(now),
            new Mock<IAuthorizationEventPublisher>(),
            terminalBroker: terminalBroker);

        await worker.ExpirePermissionsOnceAsync();

        terminalBroker.Verify(
            broker => broker.CloseAsync(permission.Id, It.IsAny<string>()),
            Times.Once);
    }

    /// <summary>
    /// A failed cloud revoke must not overwrite the reason the GRANT failed -
    /// that diagnostic is what operators need when provisioning misbehaves.
    /// </summary>
    [Fact]
    public async Task ExpirePermissionsOnceAsync_PreservesGrantFailureDetail()
    {
        var now = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        await using var fixture = CreateFixture(now);
        var permission = fixture.AddPermission(now);
        permission.ProvisioningStatus = "LOCAL_ONLY";
        permission.ProvisioningDetail = "Azure provisioning is not configured; local-only session.";
        await fixture.Context.SaveChangesAsync();

        var provisioner = new Mock<IAzureJitProvisioner>();
        provisioner
            .Setup(item => item.RevokeAsync(It.IsAny<TemporaryPermission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JitProvisioningResult(false, null, "Revoke failed."));

        var worker = fixture.CreateWorker(
            new TestClock(now),
            new Mock<IAuthorizationEventPublisher>(),
            provisioner);

        await worker.ExpirePermissionsOnceAsync();

        var saved = await fixture.Context.TemporaryPermissions
            .AsNoTracking()
            .SingleAsync(item => item.Id == permission.Id);
        Assert.Equal("LOCAL_ONLY", saved.ProvisioningStatus);
        Assert.Equal(
            "Azure provisioning is not configured; local-only session.",
            saved.ProvisioningDetail);
    }

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
            Mock<IAuthorizationEventPublisher> publisher,
            Mock<IAzureJitProvisioner>? provisioner = null,
            Mock<IJitTerminalBroker>? terminalBroker = null)
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
                publisher.Object,
                provisioner?.Object ?? Mock.Of<IAzureJitProvisioner>(),
                terminalBroker?.Object ?? Mock.Of<IJitTerminalBroker>());
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