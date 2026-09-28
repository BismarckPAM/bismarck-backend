using Notification.Service.DTOs;

namespace Notification.Service.Services;

public interface INotificationService
{
    Task<PagedResult<NotificationResponseDto>> GetUserNotificationsAsync(
        Guid userId, 
        NotificationQueryParameters query, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a single notification as read for the given user.
    /// Returns false when the notification does not exist or does not
    /// belong to the caller (so the caller can answer 404 without leaking
    /// the existence of other users' rows).
    /// </summary>
    Task<bool> MarkAsReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken = default);

    /// <summary>Marks every unread notification for the user as read.</summary>
    /// <returns>The number of rows that were updated.</returns>
    Task<int> MarkAllAsReadAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}