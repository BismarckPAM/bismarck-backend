using Microsoft.EntityFrameworkCore;
using Notification.Service.Data;
using Notification.Service.DTOs;
using Notification.Service.Models;
using Notification.Service.Services;
using Xunit;

namespace Notification.Service.Tests;

public sealed class NotificationServiceTests
{
    [Fact]
    public async Task GetUserNotificationsAsync_ReturnsOnlyRequestedUsersNotifications()
    {
        await using var context = CreateContext();
        var userId = Guid.NewGuid();
        AddNotification(context, userId, "ApprovalGranted", DateTimeOffset.UtcNow);
        AddNotification(context, Guid.NewGuid(), "AccessDenied", DateTimeOffset.UtcNow);
        var service = new NotificationService(context);

        var result = await service.GetUserNotificationsAsync(
            userId,
            new NotificationQueryParameters());

        var item = Assert.Single(result.Items);
        Assert.Equal("ApprovalGranted", item.EventType);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task GetUserNotificationsAsync_ReturnsNewestFirst()
    {
        await using var context = CreateContext();
        var userId = Guid.NewGuid();
        var older = AddNotification(context, userId, "AccessRequested", DateTimeOffset.UtcNow.AddMinutes(-5));
        var newer = AddNotification(context, userId, "ApprovalGranted", DateTimeOffset.UtcNow);
        var service = new NotificationService(context);

        var result = await service.GetUserNotificationsAsync(
            userId,
            new NotificationQueryParameters());

        Assert.Equal(newer.Id, result.Items[0].Id);
        Assert.Equal(older.Id, result.Items[1].Id);
    }

    [Fact]
    public async Task GetUserNotificationsAsync_PaginatesResults()
    {
        await using var context = CreateContext();
        var userId = Guid.NewGuid();
        for (var index = 0; index < 5; index++)
        {
            AddNotification(context, userId, $"Event{index}", DateTimeOffset.UtcNow.AddMinutes(index));
        }

        var service = new NotificationService(context);
        var result = await service.GetUserNotificationsAsync(
            userId,
            new NotificationQueryParameters { Page = 2, PageSize = 2 });

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task GetUserNotificationsAsync_ClampsInvalidPagingValues()
    {
        await using var context = CreateContext();
        var userId = Guid.NewGuid();
        AddNotification(context, userId, "AccessGranted", DateTimeOffset.UtcNow);
        var service = new NotificationService(context);

        var result = await service.GetUserNotificationsAsync(
            userId,
            new NotificationQueryParameters { Page = 0, PageSize = 101 });

        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetUserNotificationsAsync_PreservesReadStatus()
    {
        await using var context = CreateContext();
        var userId = Guid.NewGuid();
        AddNotification(context, userId, "PermissionRevoked", DateTimeOffset.UtcNow, isRead: true);
        var service = new NotificationService(context);

        var result = await service.GetUserNotificationsAsync(
            userId,
            new NotificationQueryParameters());

        Assert.True(Assert.Single(result.Items).IsRead);
    }

    private static NotificationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new NotificationDbContext(options);
    }

    private static NotificationLog AddNotification(
        NotificationDbContext context,
        Guid userId,
        string eventType,
        DateTimeOffset createdAt,
        bool isRead = false)
    {
        var notification = new NotificationLog
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            UserId = userId,
            EventType = eventType,
            Title = eventType,
            Message = $"Message for {eventType}",
            ResourceId = Guid.NewGuid().ToString(),
            CreatedAt = createdAt,
            IsRead = isRead
        };
        context.Notifications.Add(notification);
        context.SaveChanges();
        return notification;
    }
}
