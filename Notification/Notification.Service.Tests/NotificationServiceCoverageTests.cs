using Microsoft.EntityFrameworkCore;
using Notification.Service.Data;
using Notification.Service.Models;
using Notification.Service.Services;
using Xunit;

namespace Notification.Service.Tests;

public sealed class NotificationServiceCoverageTests
{
    [Fact]
    public async Task GetUserNotifications_NormalizesPagingAndOrdersNewestFirst()
    {
        var userId = Guid.NewGuid();
        await using var context = CreateContext(
            new NotificationLog
            {
                Id = Guid.NewGuid(), EventId = Guid.NewGuid(), UserId = userId,
                EventType = "A", Title = "old", Message = "old",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            },
            new NotificationLog
            {
                Id = Guid.NewGuid(), EventId = Guid.NewGuid(), UserId = userId,
                EventType = "B", Title = "new", Message = "new",
                CreatedAt = DateTimeOffset.UtcNow
            });
        var result = await new NotificationService(context).GetUserNotificationsAsync(
            userId, new() { Page = 0, PageSize = 101 });

        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal("new", result.Items[0].Title);
    }

    [Fact]
    public async Task MarkAsRead_IsScopedAndIdempotent()
    {
        var userId = Guid.NewGuid();
        var notification = new NotificationLog
        {
            Id = Guid.NewGuid(), EventId = Guid.NewGuid(), UserId = userId,
            EventType = "A", Title = "title", Message = "message"
        };
        await using var context = CreateContext(notification);
        var service = new NotificationService(context);

        Assert.True(await service.MarkAsReadAsync(userId, notification.Id));
        Assert.True(await service.MarkAsReadAsync(userId, notification.Id));
        Assert.False(await service.MarkAsReadAsync(Guid.NewGuid(), notification.Id));
    }

    [Fact]
    public async Task MarkAllAsRead_UpdatesOnlyUnreadRows()
    {
        var userId = Guid.NewGuid();
        await using var context = CreateContext(
            new NotificationLog { Id = Guid.NewGuid(), EventId = Guid.NewGuid(), UserId = userId },
            new NotificationLog { Id = Guid.NewGuid(), EventId = Guid.NewGuid(), UserId = userId, IsRead = true },
            new NotificationLog { Id = Guid.NewGuid(), EventId = Guid.NewGuid(), UserId = Guid.NewGuid() });
        var service = new NotificationService(context);

        Assert.Equal(1, await service.MarkAllAsReadAsync(userId));
        Assert.Equal(0, await service.MarkAllAsReadAsync(userId));
    }

    private static NotificationDbContext CreateContext(params NotificationLog[] rows)
    {
        var context = new NotificationDbContext(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.Notifications.AddRange(rows);
        context.SaveChanges();
        return context;
    }
}
