using System.Collections.Concurrent;
using AuthorizationService.Models;
using Microsoft.Extensions.Options;

namespace AuthorizationService.Services;

/// <summary>
/// Default broker implementation. Opens one <see cref="SshClient"/> per live
/// terminal and keeps a registry so expiry/revocation can force them closed.
/// </summary>
public sealed class JitTerminalBroker(
    IOptions<JitSshOptions> options,
    ILogger<JitTerminalBroker> logger) : IJitTerminalBroker
{
    private readonly ConcurrentDictionary<Guid, IClosableSession> _live = new();
    private readonly JitSshOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<IJitTerminalSession> OpenAsync(
        TemporaryPermission permission,
        Guid callerUserId,
        bool callerIsAdmin,
        int columns,
        int rows,
        CancellationToken cancellationToken = default)
    {
        // The JIT session must still be live. This is the whole point: once the
        // permission expires the broker refuses, so access stops.
        if (permission.Status != TemporaryPermissionStatus.ACTIVE
            || permission.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new TerminalRefusedException(
                TerminalRefusal.NotActive,
                "This JIT session is not active, so a terminal cannot be opened.");
        }

        // A user may only broker their own session. Admins may broker any.
        if (!callerIsAdmin && permission.UserId != callerUserId)
        {
            throw new TerminalRefusedException(
                TerminalRefusal.Forbidden,
                "You can only open a terminal for your own JIT session.");
        }

        if (!IsConfigured)
        {
            throw new TerminalRefusedException(
                TerminalRefusal.NotConfigured,
                "The brokered terminal is not configured: supply JitSsh__PrivateKeyPath "
                + "(or JitSsh__PrivateKey), plus JitSsh__PrivateKeyDirectory for per-user keys.");
        }

        if (string.IsNullOrWhiteSpace(permission.TargetHost))
        {
            throw new TerminalRefusedException(
                TerminalRefusal.HostUnavailable,
                "This JIT session has no target host configured.");
        }

        // Log in AS THE SESSION'S OWN USER, not a hardcoded shared account. This is
        // what makes the broker match the `ssh user@gmail@vm` command the UI shows:
        // Azure provisions an AAD account named after the email, and that account
        // is the one entitled to the JIT role assignment.
        var login = _options.ResolveLogin(permission.UserEmail);

        if (string.IsNullOrWhiteSpace(login))
        {
            throw new TerminalRefusedException(
                TerminalRefusal.NotConfigured,
                "No SSH login could be resolved for this session. Set JitSsh__Username, "
                + "or ensure the JIT session carries a user email.");
        }

        if (!_options.TryResolveKey(login, out var pem, out var keyPath))
        {
            throw new TerminalRefusedException(
                TerminalRefusal.NotConfigured,
                $"No SSH private key is configured for login '{login}'. Add a key file named "
                + $"'{login}' under JitSsh__PrivateKeyDirectory, or set a shared "
                + "JitSsh__PrivateKeyPath.");
        }

        return await SshTerminalSession.ConnectAsync(
            permission, callerUserId, login, pem, keyPath, _options, columns, rows, _live, logger,
            cancellationToken);
    }

    public async Task CloseAsync(Guid permissionId, string reason)
    {
        if (_live.TryRemove(permissionId, out var session))
        {
            logger.LogInformation(
                "Closing brokered terminal for PermissionId {PermissionId}: {Reason}",
                permissionId, reason);
            await session.CloseAsync(reason);
        }
    }

    /// <summary>Internal handle so the broker can force a session closed.</summary>
    internal interface IClosableSession
    {
        Task CloseAsync(string reason);
    }
}