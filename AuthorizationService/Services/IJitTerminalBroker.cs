using AuthorizationService.Models;

namespace AuthorizationService.Services;

/// <summary>
/// A live brokered SSH session. Exists only while the underlying JIT permission
/// is ACTIVE; <see cref="Dispose"/> tears the channel down.
/// </summary>
public interface IJitTerminalSession : IAsyncDisposable
{
    Guid PermissionId { get; }
    string UserId { get; }
    /// <summary>Reads whatever the remote shell has produced since the last call.</summary>
    Task<string> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Forwards keystrokes/control sequences to the remote shell.</summary>
    void Write(string data);

    /// <summary>Propagates a browser terminal resize to the remote PTY.</summary>
    void Resize(int columns, int rows);
}

/// <summary>
/// Brokers privileged SSH sessions on the user's behalf.
///
/// The private key is held by the service and is NEVER sent to the browser. The
/// only route to the machine is through here, which is what makes JIT expiry a
/// real control: when the permission expires the broker kills the channel and the
/// user's shell dies immediately.
/// </summary>
public interface IJitTerminalBroker
{
    /// <summary>Whether a held credential is configured for the brokered terminal.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Opens a PTY session to the target VM. Throws if the session is not ACTIVE,
    /// the caller does not own it, or the broker has no credential.
    /// </summary>
    Task<IJitTerminalSession> OpenAsync(
        TemporaryPermission permission,
        Guid callerUserId,
        bool callerIsAdmin,
        int columns,
        int rows,
        CancellationToken cancellationToken = default);

    /// <summary>Closes any live session for a permission (called on expiry/revoke).</summary>
    Task CloseAsync(Guid permissionId, string reason);
}

/// <summary>Reasons a brokered terminal can be refused.</summary>
public enum TerminalRefusal
{
    NotConfigured,
    NotActive,
    Forbidden,
    HostUnavailable,
    AuthenticationFailed
}

public sealed class TerminalRefusedException(TerminalRefusal refusal, string message) : Exception(message)
{
    public TerminalRefusal Refusal { get; } = refusal;
}