using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notification.Service.DTOs;
using Notification.Service.Models;
using Notification.Service.Services;

namespace Notification.Service.Controllers;

[Authorize]
[ApiController]
[Route("/api/notifications/{userId}")]
public class NotificationsController(INotificationService notificationService) : ControllerBase
{
    // GET /api/notifications/{userId}?page=1&pageSize=10
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<NotificationResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<NotificationResponseDto>>> GetNotifications(
        [FromRoute] Guid userId,
        [FromQuery] NotificationQueryParameters query, 
        CancellationToken cancellationToken)
    {
     var result = await notificationService.GetUserNotificationsAsync(userId, query, cancellationToken);        return Ok(result);
    }

    // No create POST, PUT, PATCH, or DELETE
    // Notification logs must remain strictly immutable
}