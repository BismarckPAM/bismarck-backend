using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Notification.Service.Controllers;
using Notification.Service.DTOs;
using Notification.Service.Services;
using Xunit;

namespace Notification.Service.Tests;

public sealed class NotificationsControllerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task GetMyNotifications_UsesAuthenticatedUser()
    {
        var service = new Mock<INotificationService>();
        var expected = new PagedResult<NotificationResponseDto>([], 0, 1, 20);
        service.Setup(item => item.GetUserNotificationsAsync(
                UserId, It.IsAny<NotificationQueryParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var controller = CreateController(service);

        var result = await controller.GetMyNotifications(new NotificationQueryParameters(), CancellationToken.None);

        Assert.Same(expected, Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    [Fact]
    public async Task MarkAsRead_ReturnsNoContentOrNotFound()
    {
        var service = new Mock<INotificationService>();
        var id = Guid.NewGuid();
        service.Setup(item => item.MarkAsReadAsync(UserId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var controller = CreateController(service);

        Assert.IsType<NoContentResult>(
            await controller.MarkAsRead(id, CancellationToken.None));

        service.Setup(item => item.MarkAsReadAsync(UserId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        Assert.IsType<NotFoundResult>(
            await controller.MarkAsRead(id, CancellationToken.None));
    }

    [Fact]
    public async Task LegacyGet_ForbidsOtherUserUnlessAdmin()
    {
        var service = new Mock<INotificationService>();
        var controller = CreateController(service);

        Assert.IsType<ForbidResult>(
            (await controller.GetNotifications(Guid.NewGuid(), new(), CancellationToken.None)).Result);

        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, UserId.ToString()), new Claim(ClaimTypes.Role, "Admin")],
                "test"));
        var result = await controller.GetNotifications(Guid.NewGuid(), new(), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task MarkAllAsRead_ReturnsNoContent()
    {
        var service = new Mock<INotificationService>();
        var controller = CreateController(service);

        Assert.IsType<NoContentResult>(
            await controller.MarkAllAsRead(CancellationToken.None));
        service.Verify(item => item.MarkAllAsReadAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static NotificationsController CreateController(Mock<INotificationService> service)
    {
        var controller = new NotificationsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "test"))
                }
            }
        };
        return controller;
    }
}
