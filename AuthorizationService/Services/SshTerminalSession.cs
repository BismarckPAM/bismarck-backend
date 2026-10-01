using System.Collections.Concurrent;
using System.Text;
using AuthorizationService.Models;
using Renci.SshNet;

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
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Guid PermissionId => _permissionId;
    public string UserId { get; }
    public Task Completion => _completion.Task;

    public SshTerminalSession(
        TemporaryPermission permission,
        Guid callerUserId,
        JitSshOptions options,
        int columns,
        int rows,
        ConcurrentDictionary<Guid, JitTerminalBroker.IClosableSession> registry,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        _permissionId = permission.Id;
        UserId = callerUserId.ToString();
        _registry = registry;
        _logger = logger;

        // The held key. Used here and never returned to the caller. The constructor
        // overload taking the PEM as a string means we never write the key to
        // disk inside the container.
        var keySource = new IPrivateKeySource[]
        {
            new PrivateKeyFile(options.PrivateKey!.Replace("\r\n", "\n"))
        };

        _client = new SshClient(
            permission.TargetHost!, options.Port, options.Username!, keySource);

        // Bound the connect by the caller's token so a hung TCP handshake cannot
        // pin a browser tab open indefinitely.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        _client.ConnectAsync(timeout.Token).GetAwaiter().GetResult();

        _shell = _client.CreateShellStream(
            terminalName: "xterm",
            columns: (uint)Math.Clamp(columns, 20, 500),
            rows: (uint)Math.Clamp(rows, 5, 200),
            width: 0,
            height: 0,
            bufferSize: 0x4000);

        _registry[_permissionId] = this;
    }

    /// <summary>Reads whatever the remote shell has produced so far.</summary>
    public async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        if (!_shell.CanRead)
        {
            _completion.TrySetResult();
            return string.Empty;
        }

        var buffer = new byte[8192];
        var read = await _shell.ReadAsync(buffer, cancellationToken);
        if (read <= 0)
        {
            _completion.TrySetResult();
            return string.Empty;
        }

        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    /// <summary>Forwards keystrokes or control sequences to the remote shell.</summary>
    public void Write(string data)
    {
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

    public async Task CloseAsync(string reason)
    {
        try { _shell.Close(); _shell.Dispose(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Error closing shell for {PermissionId}.", _permissionId); }

        try
        {
            if (_client.IsConnected)
                _client.Disconnect();
            _client.Dispose();
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Error disconnecting SSH client for {PermissionId}.", _permissionId); }

        _registry.TryRemove(_permissionId, out _);
        _completion.TrySetResult();
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await CloseAsync("session ended");
}