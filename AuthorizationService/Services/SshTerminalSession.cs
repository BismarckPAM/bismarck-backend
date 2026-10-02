using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using AuthorizationService.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace AuthorizationService.Services;

/// <summary>
/// One live PTY session on the target VM, authenticated with the held private
/// key. The key is used here and never leaves the process.
/// </summary>
internal sealed class SshTerminalSession : IJitTerminalSession, JitTerminalBroker.IClosableSession
{
    private readonly SshClient _client;
    private readonly ShellStream _shell;
    private readonly ConcurrentDictionary<Guid, JitTerminalBroker.IClosableSession> _registry;
    private readonly ILogger _logger;
    private readonly Guid _permissionId;
    private int _closed;

    public Guid PermissionId => _permissionId;
    public string UserId { get; }

    /// <summary>The SSH login this session authenticated as (the session's own user).</summary>
    public string Login { get; }

    private SshTerminalSession(
        SshClient client,
        ShellStream shell,
        TemporaryPermission permission,
        Guid callerUserId,
        string login,
        ConcurrentDictionary<Guid, JitTerminalBroker.IClosableSession> registry,
        ILogger logger)
    {
        _client = client;
        _shell = shell;
        _permissionId = permission.Id;
        UserId = callerUserId.ToString();
        Login = login;
        _registry = registry;
        _logger = logger;

        // Take ownership of the slot atomically. Two browser tabs racing on the
        // same permission cannot both win: the previous holder is marked closed
        // and replaced here. Previously the loser was simply overwritten and left
        // unreachable by CloseAsync, so revoke/expiry could not kill that channel.
        _registry.AddOrUpdate(
            _permissionId,
            this,
            (_, existing) =>
            {
                if (existing is SshTerminalSession previous)
                    Interlocked.Exchange(ref previous._closed, 1);
                return this;
            });
    }

    /// <summary>Reads whatever the remote shell has produced so far.</summary>
    public async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _closed) == 1 || !_shell.CanRead)
        {
            return string.Empty;
        }

        var buffer = new byte[8192];
        var read = await _shell.ReadAsync(buffer, cancellationToken);
        if (read <= 0)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    /// <summary>Forwards keystrokes or control sequences to the remote shell.</summary>
    public void Write(string data)
    {
        if (Volatile.Read(ref _closed) == 1)
            return;

        try
        {
            _shell.Write(data);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Terminal write failed for PermissionId {PermissionId}.", _permissionId);
        }
    }

    public void Resize(int columns, int rows)
    {
        if (Volatile.Read(ref _closed) == 1)
            return;

        try
        {
            _shell.ChangeWindowSize(
                (uint)Math.Clamp(columns, 20, 500),
                (uint)Math.Clamp(rows, 5, 200),
                0,
                0);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Terminal resize failed for PermissionId {PermissionId}.", _permissionId);
        }
    }

    public Task CloseAsync(string reason)
    {
        // Idempotent: the browser closing its socket while an admin revokes at the
        // same moment must not double-dispose the SSH client.
        if (Interlocked.Exchange(ref _closed, 1) == 1)
            return Task.CompletedTask;

        _logger.LogInformation(
            "Closing brokered terminal for PermissionId {PermissionId} as {Login}: {Reason}",
            _permissionId, Login, reason);

        try { _shell.Close(); _shell.Dispose(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Error closing shell for {PermissionId}.", _permissionId); }

        try
        {
            if (_client.IsConnected)
                _client.Disconnect();
            _client.Dispose();
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Error disconnecting SSH client for {PermissionId}.", _permissionId); }

        // Only clear the slot while it is still ours: a newer terminal may have
        // already taken this permission over, and it must survive our teardown.
        _registry.TryRemove(
            new KeyValuePair<Guid, JitTerminalBroker.IClosableSession>(_permissionId, this));

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await CloseAsync("session ended");

    /// <summary>
    /// Opens the TCP/SSH/auth handshake and the remote PTY. Fully asynchronous:
    /// the handshake is bounded by <see cref="JitSshOptions.ConnectTimeoutSeconds"/>
    /// and translated into a <see cref="TerminalRefusedException"/> so the caller
    /// gets an actionable 403 instead of an opaque 500.
    /// </summary>
    public static async Task<SshTerminalSession> ConnectAsync(
        TemporaryPermission permission,
        Guid callerUserId,
        string login,
        string? pem,
        string? keyPath,
        JitSshOptions options,
        int columns,
        int rows,
        ConcurrentDictionary<Guid, JitTerminalBroker.IClosableSession> registry,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var keySource = BuildKeySource(login, pem, keyPath, options.Passphrase);

        // SSH.NET 2026 requires the username and auth methods on ConnectionInfo,
        // so the login resolved for THIS session flows straight into the handshake.
        // Fully qualified: ASP.NET also defines a ConnectionInfo.
        var connectionInfo = new Renci.SshNet.ConnectionInfo(
            permission.TargetHost!,
            options.Port,
            login,
            [keySource]);

        var client = new SshClient(connectionInfo)
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30)
        };

        var expectedFingerprint = JitSshOptions.NormalizeFingerprint(options.HostKeyFingerprint);
        if (expectedFingerprint is not null)
        {
            client.HostKeyReceived += (_, args) =>
            {
                // Pin the host key: this is what stops a DNS/IP hijack from
                // silently capturing the session. Implemented here rather than
                // only documented, because an unenforced option is not a control.
                if (!string.Equals(
                        args.FingerPrintSHA256,
                        expectedFingerprint,
                        StringComparison.OrdinalIgnoreCase))
                {
                    args.CanTrust = false;
                    logger.LogWarning(
                        "Host key mismatch for {Host}: expected SHA256:{Expected}, presented SHA256:{Actual}.",
                        permission.TargetHost, expectedFingerprint, args.FingerPrintSHA256);
                }
            };
        }

        try
        {
            // Bound the connect by the caller's token so a hung TCP handshake
            // cannot pin a browser tab open indefinitely.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(
                Math.Clamp(options.ConnectTimeoutSeconds, 5, 120)));

            await client.ConnectAsync(timeout.Token);
        }
        catch (Exception exception)
        {
            client.Dispose();
            throw Translate(exception, login, permission.TargetHost!, logger);
        }

        try
        {
            var shell = client.CreateShellStream(
                terminalName: "xterm-256color",
                columns: (uint)Math.Clamp(columns, 20, 500),
                rows: (uint)Math.Clamp(rows, 5, 200),
                width: 0,
                height: 0,
                bufferSize: 0x4000);

            return new SshTerminalSession(
                client, shell, permission, callerUserId, login, registry, logger);
        }
        catch (Exception exception)
        {
            client.Dispose();
            throw Translate(exception, login, permission.TargetHost!, logger);
        }
    }

    // Returns the concrete type: IAuthenticationMethod is internal to SSH.NET,
    // so it cannot appear in the signature of an internal-but-referenced member.
    private static PrivateKeyAuthenticationMethod BuildKeySource(
        string login,
        string? pem,
        string? keyPath,
        string? passphrase)
    {
        PrivateKeyFile key;

        if (!string.IsNullOrWhiteSpace(pem))
        {
            // IMPORTANT: in SSH.NET the PrivateKeyFile(string) overload is a
            // FILENAME. The previous code passed PEM text there, so an inline key
            // was silently interpreted as a path and every session failed with
            // "key not found". Inline PEM must go through the Stream overload.
            key = string.IsNullOrEmpty(passphrase)
                ? new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(pem)))
                : new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(pem)), passphrase);
        }
        else if (!string.IsNullOrWhiteSpace(keyPath))
        {
            if (!File.Exists(keyPath))
            {
                throw new TerminalRefusedException(
                    TerminalRefusal.NotConfigured,
                    $"The configured SSH private key file '{keyPath}' does not exist.");
            }

            key = new PrivateKeyFile(keyPath, passphrase ?? string.Empty);
        }
        else
        {
            throw new TerminalRefusedException(
                TerminalRefusal.NotConfigured,
                $"No SSH private key was resolved for login '{login}'.");
        }

        return new PrivateKeyAuthenticationMethod(login, new IPrivateKeySource[] { key });
    }

    /// <summary>
    /// Maps SSH/transport failures onto the refusal vocabulary the controller
    /// already knows how to render, so the browser gets a precise reason
    /// ("key rejected for this user") instead of a generic failure.
    /// </summary>
    private static TerminalRefusedException Translate(
        Exception exception,
        string login,
        string host,
        ILogger logger)
    {
        logger.LogWarning(exception, "Brokered SSH connection to {Host} as {Login} failed.", host, login);

        return exception switch
        {
            SshAuthenticationException =>
                new TerminalRefusedException(
                    TerminalRefusal.AuthenticationFailed,
                    $"The VM rejected the SSH key for user '{login}'. Confirm the matching public "
                    + "key is in that user's ~/.ssh/authorized_keys on the VM."),

            SshOperationTimeoutException =>
                new TerminalRefusedException(
                    TerminalRefusal.HostUnavailable,
                    $"Timed out connecting to {host}:22. Is the VM running and reachable?"),

            SocketException =>
                new TerminalRefusedException(
                    TerminalRefusal.HostUnavailable,
                    $"Could not reach {host}:22. Check the VM is running, that port 22 is open, "
                    + "and that the broker has network access to the VM."),

            OperationCanceledException =>
                new TerminalRefusedException(
                    TerminalRefusal.HostUnavailable,
                    $"The connection to {host} was cancelled before it completed."),

            _ => new TerminalRefusedException(
                TerminalRefusal.HostUnavailable,
                $"Could not open a terminal on {host}: {exception.Message}")
        };
    }
}