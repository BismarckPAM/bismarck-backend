using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AuthorizationService.Data;
using AuthorizationService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    ILogger<JitTerminalController> logger) : ControllerBase
{
    [HttpGet("{id:guid}/status")]
    public IActionResult Status(Guid id)
        => Ok(new { permissionId = id, brokerConfigured = broker.IsConfigured });

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

        var outputPump = SendOutputAsync(cancellationToken);

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

                var message = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
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
            await session.DisposeAsync();
            try { await outputPump; } catch { /* socket already gone */ }
            await SendAsync(new { t = "e", m = "Session closed." }, CancellationToken.None);
        }
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