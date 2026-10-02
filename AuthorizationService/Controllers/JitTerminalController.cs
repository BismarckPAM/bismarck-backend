using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AuthorizationService.Data;
using AuthorizationService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AuthorizationService.Controllers;

/// <summary>
/// Brokered terminal for an active JIT session.
///
/// The client speaks WebSocket; this service owns the SSH channel to the VM using
/// a held private key. The key is never sent to the browser - the only route to
/// the machine is through here, so when the JIT permission expires the channel is
/// killed and the user's shell dies with it.
///
/// Wire protocol (JSON text frames both ways):
///   client -> {"t":"i","s":"ls -la"}   send input
///            {"t":"i","c":80,"r":24}    init with terminal size
///            {"t":"r","c":120,"r":40}    resize
///   server -> {"t":"o","d":"..."}        output
///            {"t":"e","m":"..."}        closed / error
/// </summary>
[ApiController]
[Authorize]
[Route("api/jit/terminal")]
public sealed class JitTerminalController(
    AuthorizationDbContext dbContext,
    IJitTerminalBroker broker,
    IOptions<JitSshOptions> options,
    ILogger<JitTerminalController> logger) : ControllerBase
{
    private readonly JitSshOptions _options = options.Value;
    /// <summary>
    /// Reports whether the broker holds a usable credential, and which SSH login
    /// it would use for this session. The frontend calls this to decide whether to
    /// offer the terminal at all, instead of failing after the user clicks.
    /// </summary>
    [HttpGet("{id:guid}/status")]
    public async Task<IActionResult> Status(
        Guid id,
        [FromQuery] string? userEmail,
        CancellationToken cancellationToken)
    {
        var permission = await dbContext.TemporaryPermissions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (permission is null)
            return NotFound(new { message = $"JIT session '{id}' was not found." });

        var login = _options.ResolveLogin(userEmail ?? permission.UserEmail);
        var hasKey = login is not null && _options.TryResolveKey(login, out _, out _);

        return Ok(new
        {
            permissionId = id,
            brokerConfigured = broker.IsConfigured,
            login,
            keyAvailable = hasKey,
            // One reason the UI can show verbatim, so the operator does not have to
            // read container logs to find out which secret is missing.
            unavailableReason = broker.IsConfigured
                ? hasKey
                    ? null
                    : $"No SSH private key is configured for login '{login}'."
                : "The brokered terminal is not configured on the Authorization Service."
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Connect(
        Guid id,
        [FromQuery] int columns = 80,
        [FromQuery] int rows = 24,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            return StatusCode(StatusCodes.Status400BadRequest, new
            {
                error = "This endpoint requires a WebSocket connection."
            });
        }

        var (callerId, isAdmin) = ResolveCaller();
        if (callerId is null)
            return Forbid();

        var permission = await dbContext.TemporaryPermissions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (permission is null)
            return NotFound(new { message = $"JIT session '{id}' was not found." });

        IJitTerminalSession session;
        try
        {
            session = await broker.OpenAsync(
                permission, callerId.Value, isAdmin, columns, rows, cancellationToken);
        }
        catch (TerminalRefusedException refused)
        {
            logger.LogWarning(
                "Refused brokered terminal for PermissionId {PermissionId}: {Refusal}",
                id, refused.Refusal);
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = refused.Refusal.ToString(),
                message = refused.Message
            });
        }

        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await PumpAsync(socket, session, cancellationToken);
        return new EmptyResult();
    }
private async Task PumpAsync(
        WebSocket socket,
        IJitTerminalSession session,
        CancellationToken cancellationToken)
    {
        var receiveBuffer = new byte[8192];

        // A dedicated token so teardown can stop the output pump deterministically.
        // The previous code awaited the pump AFTER disposing the SSH session and
        // without a cancellation signal, so closing the browser tab could leave the
        // pump blocked in ShellStream.ReadAsync holding the request open.
        using var pumpLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        async Task SendAsync(object frame, CancellationToken ct)
        {
            if (socket.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(frame));
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }

        async Task SendOutputAsync(CancellationToken ct)
        {
            try
            {
                while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var output = await session.ReadAsync(ct);
                    if (output.Length == 0) break;
                    await SendAsync(new { t = "o", d = output }, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Terminal output pump ended for PermissionId {PermissionId}.",
                    session.PermissionId);
            }
        }

        var outputPump = SendOutputAsync(pumpLifetime.Token);

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(receiveBuffer, cancellationToken);
                }
                catch (WebSocketException)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                // Reassemble fragmented frames: a paste or a burst of output can
                // arrive split across several frames, and parsing each partial
                // buffer separately silently dropped input.
                var message = result.MessageType == WebSocketMessageType.Text
                    ? await ReadTextFrameAsync(socket, receiveBuffer, result, cancellationToken)
                    : string.Empty;

                if (string.IsNullOrWhiteSpace(message))
                    continue;

                JsonElement frame;
                try
                {
                    frame = JsonDocument.Parse(message).RootElement.Clone();
                }
                catch (JsonException)
                {
                    continue;
                }

                var type = frame.TryGetProperty("t", out var t) ? t.GetString() : null;
                switch (type)
                {
                    case "i":
                        if (frame.TryGetProperty("s", out var input) && input.GetString() is { } text)
                        {
                            session.Write(text);
                        }
                        else if (frame.TryGetProperty("c", out var c) && frame.TryGetProperty("r", out var r))
                        {
                            session.Resize(c.GetInt32(), r.GetInt32());
                        }
                        break;

                    case "r":
                        if (frame.TryGetProperty("c", out var nc) && frame.TryGetProperty("r", out var nr))
                        {
                            session.Resize(nc.GetInt32(), nr.GetInt32());
                        }
                        break;
                }
            }
        }
        finally
        {
            // Tell the client the session is over BEFORE tearing the channel down,
            // then stop the pump and release the SSH session.
            try
            {
                await SendAsync(new { t = "e", m = "Session closed." }, CancellationToken.None);
            }
            catch
            {
                // Socket already gone - nothing useful to report.
            }

            await pumpLifetime.CancelAsync();
            await session.DisposeAsync();
            try { await outputPump; } catch { /* socket already gone */ }
        }
    }

    /// <summary>
    /// Reads one complete text frame, continuing to receive while the current
    /// message is fragmented.
    /// </summary>
    private static async Task<string> ReadTextFrameAsync(
        WebSocket socket,
        byte[] buffer,
        WebSocketReceiveResult first,
        CancellationToken cancellationToken)
    {
        using var payload = new MemoryStream();
        payload.Write(buffer, 0, first.Count);

        var result = first;
        while (!result.EndOfMessage)
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                break;

            payload.Write(buffer, 0, result.Count);
        }

        return Encoding.UTF8.GetString(payload.ToArray());
    }

    private (Guid? UserId, bool IsAdmin) ResolveCaller()
    {
        var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value
            ?? User.FindFirst("role")?.Value;

        var isAdmin = User.IsInRole("Admin")
            || string.Equals(roleClaim, "Admin", StringComparison.OrdinalIgnoreCase);

        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("userId")?.Value;

        return (Guid.TryParse(idClaim, out var parsed) ? parsed : null, isAdmin);
    }
}