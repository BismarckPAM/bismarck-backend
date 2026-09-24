using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notification.Service.DTOs;
using Notification.Service.Models;
using Notification.Service.Services;

namespace Audit.Service.Controllers;

[Authorize]
[ApiController]
[Route("/api/notifications/{userId}")]
public class NotificationsController(INotificationService notificationService) : ControllerBase
{
    // GET /api/notifications/{userId}?page=1&pageSize=10
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<Notification>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<Notification>>> GetNotifications(
        [FromRoute] Guid userId,
        [FromQuery] AuditLogQueryParameters query, 
        CancellationToken cancellationToken)
    {
        var result = await  notificationService.GetLogsAsync(query, cancellationToken);
        return Ok(result);
    }

    // No create POST, PUT, PATCH, or DELETE
    // Notification logs must remain strictly immutable
}