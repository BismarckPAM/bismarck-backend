using Microsoft.EntityFrameworkCore;
using Notification.Service.Data;
using Notification.Service.DTOs;

namespace Notification.Service.Services;

public class NotificationService(NotificationDbContext dbContext) : INotificationService
{
    public async Task<PagedResult<NotificationResponseDto>> GetUserNotificationsAsync(
        Guid userId, 
        NotificationQueryParameters query, 
        CancellationToken cancellationToken = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var baseQuery = dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId);

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        // Fetch newest first with pagination and project directly to DTO
        var items = await baseQuery
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationResponseDto(
                n.Id,
                n.EventType,
                n.Title,
                n.Message,
                n.CreatedAt,
                n.IsRead
            ))
            .ToListAsync(cancellationToken);

        return new PagedResult<NotificationResponseDto>(items, totalCount, page, pageSize);
    }
}