using System.Collections.Concurrent;
using AuthorizationService.Models;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace AuthorizationService.Services;

/// <summary>Configuration for the held SSH credential used by the brokered terminal.</summary>
public sealed class JitSshOptions
{
    public const string SectionName = "JitSsh";

    /// <summary>PEM private key. Supplied via <c>JitSsh__PrivateKey</c>, never committed.</summary>
    public string? PrivateKey { get; init; }

    public string? Username { get; init; }
    public int Port { get; init; } = 22;

    /// <summary>
    /// Optional SHA256 fingerprint of the VM host key. When set, the broker
    /// refuses to connect unless the server presents exactly this key - this is
    /// what stops a DNS/IP hijack from silently capturing the session.
    /// </summary>
    public string? HostKeyFingerprint { get; init; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PrivateKey) && !string.IsNullOrWhiteSpace(Username);
}

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

    public Task<IJitTerminalSession> OpenAsync(
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
                "The brokered terminal is not configured: JitSsh__PrivateKey and "
                + "JitSsh__Username are required on this service.");
        }

        if (string.IsNullOrWhiteSpace(permission.TargetHost))
        {
            throw new TerminalRefusedException(
                TerminalRefusal.HostUnavailable,
                "This JIT session has no target host configured.");
        }

        var session = new SshTerminalSession(
            permission, callerUserId, _options, columns, rows, _live, logger, cancellationToken);

        return Task.FromResult<IJitTerminalSession>(session);
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