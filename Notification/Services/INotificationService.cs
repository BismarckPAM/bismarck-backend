using Notification.Service.DTOs;

namespace Notification.Service.Services;

public interface INotificationService
{
    Task<PagedResult<NotificationResponseDto>> GetUserNotificationsAsync(
        Guid userId, 
        NotificationQueryParameters query, 
        CancellationToken cancellationToken = default);
}