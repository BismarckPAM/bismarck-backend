using AuthorizationService.Models;
using AuthorizationService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthorizationService.Tests;

public sealed class JitTerminalBrokerTests
{
    [Fact]
    public async Task OpenAsync_RejectsExpiredSession()
    {
        var broker = CreateBroker(new JitSshOptions { PrivateKey = "key", Username = "user" });
        var permission = Permission();
        permission.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        var exception = await Assert.ThrowsAsync<TerminalRefusedException>(() =>
            broker.OpenAsync(permission, permission.UserId, false, 80, 24));

        Assert.Equal(TerminalRefusal.NotActive, exception.Refusal);
    }

    [Fact]
    public async Task OpenAsync_RejectsOtherUsers()
    {
        var broker = CreateBroker(new JitSshOptions { PrivateKey = "key", Username = "user" });

        var exception = await Assert.ThrowsAsync<TerminalRefusedException>(() =>
            broker.OpenAsync(Permission(), Guid.NewGuid(), false, 80, 24));

        Assert.Equal(TerminalRefusal.Forbidden, exception.Refusal);
    }

    [Fact]
    public async Task OpenAsync_RejectsUnconfiguredBrokerAndMissingHost()
    {
        var permission = Permission();
        var unconfigured = CreateBroker(new JitSshOptions());
        var notConfigured = await Assert.ThrowsAsync<TerminalRefusedException>(() =>
            unconfigured.OpenAsync(permission, permission.UserId, false, 80, 24));
        Assert.Equal(TerminalRefusal.NotConfigured, notConfigured.Refusal);

        var noHost = Permission();
        noHost.TargetHost = null;
        var configured = CreateBroker(new JitSshOptions { PrivateKey = "key", Username = "user" });
        var hostUnavailable = await Assert.ThrowsAsync<TerminalRefusedException>(() =>
            configured.OpenAsync(noHost, noHost.UserId, false, 80, 24));
        Assert.Equal(TerminalRefusal.HostUnavailable, hostUnavailable.Refusal);
    }

    [Fact]
    public async Task OpenAsync_RejectsMissingLoginOrKey()
    {
        var permission = Permission();
        permission.UserEmail = null;
        var noLogin = CreateBroker(new JitSshOptions { PrivateKey = "key" });
        var loginFailure = await Assert.ThrowsAsync<TerminalRefusedException>(() =>
            noLogin.OpenAsync(permission, permission.UserId, false, 80, 24));
        Assert.Equal(TerminalRefusal.NotConfigured, loginFailure.Refusal);

        var noKey = CreateBroker(new JitSshOptions { Username = "user" });
        var keyFailure = await Assert.ThrowsAsync<TerminalRefusedException>(() =>
            noKey.OpenAsync(Permission(), Guid.Empty, true, 80, 24));
        Assert.Equal(TerminalRefusal.NotConfigured, keyFailure.Refusal);
    }

    private static JitTerminalBroker CreateBroker(JitSshOptions options) =>
        new(Options.Create(options), NullLogger<JitTerminalBroker>.Instance);

    private static TemporaryPermission Permission() => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Status = TemporaryPermissionStatus.ACTIVE,
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
        TargetHost = "127.0.0.1",
        UserEmail = "user@example.com"
    };
}
