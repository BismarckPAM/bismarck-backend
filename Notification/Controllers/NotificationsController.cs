using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notification.Service.DTOs;
using Notification.Service.Services;

namespace Notification.Service.Controllers;

[Authorize]
[ApiController]
public class NotificationsController(INotificationService notificationService) : ControllerBase
{
    // GET /api/notifications?page=1&pageSize=10
    // The user is derived from the JWT — never trusted from the URL.
    [HttpGet("api/notifications")]
    [ProducesResponseType(typeof(PagedResult<NotificationResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<NotificationResponseDto>>> GetMyNotifications(
        [FromQuery] NotificationQueryParameters query,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var result = await notificationService.GetUserNotificationsAsync(userId, query, cancellationToken);
        return Ok(result);
    }

    // POST /api/notifications/{id}/read — persistent mark-as-read.
    [HttpPost("api/notifications/{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var updated = await notificationService.MarkAsReadAsync(userId, id, cancellationToken);
        return updated ? NoContent() : NotFound();
    }

    // POST /api/notifications/read-all — mark everything read for the caller.
    [HttpPost("api/notifications/read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        await notificationService.MarkAllAsReadAsync(userId, cancellationToken);
        return NoContent();
    }

    // Legacy route retained for backward compatibility. It only ever returns
    // the caller's own notifications (or 403 for an admin viewing another
    // user) so it can no longer be used to enumerate other users' data.
    [HttpGet("/api/notifications/{userId:guid}")]
    [ProducesResponseType(typeof(PagedResult<NotificationResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<NotificationResponseDto>>> GetNotifications(
        [FromRoute] Guid userId,
        [FromQuery] NotificationQueryParameters query,
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var isAdmin = User.IsInRole("ADMIN") || User.IsInRole("Admin") || User.IsInRole("admin");

        if (userId != currentUserId && !isAdmin)
        {
            return Forbid();
        }

        var result = await notificationService.GetUserNotificationsAsync(userId, query, cancellationToken);
        return Ok(result);
    }

    private Guid GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("userId");

        if (Guid.TryParse(raw, out var userId))
        {
            return userId;
        }

        throw new UnauthorizedAccessException("The authenticated user ID is missing or invalid.");
    }
}