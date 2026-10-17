using AuthorizationService.Data;
using AuthorizationService.Models;
using AuthorizationService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Messaging;
using Xunit;

namespace AuthorizationService.Tests;

public sealed class TemporaryPermissionExpirationCoverageTests
{
    [Fact]
    public async Task ExpirePermissionsOnce_ExpiresRevokesAndPublishesExpiredPermissions()
    {
        var now = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        var permission = new TemporaryPermission
        {
            Id = Guid.NewGuid(), ApprovalId = Guid.NewGuid(), UserId = Guid.NewGuid(),
            ResourceId = Guid.NewGuid(), Status = TemporaryPermissionStatus.ACTIVE,
            ExpiresAt = now.AddMinutes(-1), CloudRoleAssignmentId = "/assignment/1"
        };
        await using var context = CreateContext(permission);
        using var provider = new ServiceCollection()
            .AddScoped<AuthorizationDbContext>(_ => context)
            .BuildServiceProvider();
        var provisioner = new Mock<IAzureJitProvisioner>();
        provisioner.Setup(item => item.RevokeAsync(permission, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JitProvisioningResult(true, permission.CloudRoleAssignmentId, "revoked"));
        var publisher = new Mock<IAuthorizationEventPublisher>();
        var worker = CreateWorker(provider, now, provisioner, publisher);

        await worker.ExpirePermissionsOnceAsync();

        Assert.Equal(TemporaryPermissionStatus.EXPIRED, permission.Status);
        Assert.Equal(now, permission.RevokedAt);
        Assert.Equal("EXPIRED", permission.ProvisioningStatus);
        publisher.Verify(item => item.PublishAsync(
            KafkaTopics.PermissionRevoked,
            It.IsAny<SecurityEvent<object>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExpirePermissionsOnce_LeavesUnexpiredRowsAlone()
    {
        var now = DateTimeOffset.UtcNow;
        var permission = new TemporaryPermission
        {
            Id = Guid.NewGuid(), Status = TemporaryPermissionStatus.ACTIVE,
            ExpiresAt = now.AddHours(1)
        };
        await using var context = CreateContext(permission);
        using var provider = new ServiceCollection()
            .AddScoped<AuthorizationDbContext>(_ => context)
            .BuildServiceProvider();
        var worker = CreateWorker(provider, now, new Mock<IAzureJitProvisioner>(),
            new Mock<IAuthorizationEventPublisher>());

        await worker.ExpirePermissionsOnceAsync();

        Assert.Equal(TemporaryPermissionStatus.ACTIVE, permission.Status);
    }

    private static TemporaryPermissionExpirationWorker CreateWorker(
        ServiceProvider provider,
        DateTimeOffset now,
        Mock<IAzureJitProvisioner> provisioner,
        Mock<IAuthorizationEventPublisher> publisher)
    {
        var broker = new Mock<IJitTerminalBroker>();
        var services = new Mock<IServiceScopeFactory>();
        services.Setup(item => item.CreateScope()).Returns(() => provider.CreateScope());
        return new(
            services.Object,
            new FixedClock(now),
            new ConfigurationBuilder().AddInMemoryCollection().Build(),
            NullLogger<TemporaryPermissionExpirationWorker>.Instance,
            publisher.Object,
            provisioner.Object,
            broker.Object);
    }

    private static AuthorizationDbContext CreateContext(TemporaryPermission permission)
    {
        var context = new AuthorizationDbContext(new DbContextOptionsBuilder<AuthorizationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.TemporaryPermissions.Add(permission);
        context.SaveChanges();
        return context;
    }

    private sealed class FixedClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
