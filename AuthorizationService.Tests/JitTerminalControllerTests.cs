using System.Security.Claims;
using AuthorizationService.Controllers;
using AuthorizationService.Data;
using AuthorizationService.Models;
using AuthorizationService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AuthorizationService.Tests;

public sealed class JitTerminalControllerTests
{
    [Fact]
    public async Task Status_ReturnsNotFoundForUnknownPermission()
    {
        await using var context = CreateContext();
        var controller = CreateController(context, configured: false);

        var result = await controller.Status(Guid.NewGuid(), null, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Status_ReportsConfiguredKeyAndResolvedLogin()
    {
        await using var context = CreateContext(new TemporaryPermission
        {
            Id = Guid.NewGuid(),
            UserEmail = "user@example.com"
        });
        var permission = await context.TemporaryPermissions.SingleAsync();
        var controller = CreateController(
            context,
            configured: true,
            new JitSshOptions
            {
                UsernameTemplate = "{email}",
                PrivateKeys = new Dictionary<string, string>
                {
                    ["user@example.com"] = "-----BEGIN KEY-----"
                }
            });

        var result = Assert.IsType<OkObjectResult>(
            await controller.Status(permission.Id, null, CancellationToken.None));
        var value = result.Value!;
        Assert.Equal("user@example.com", value.GetType().GetProperty("login")!.GetValue(value));
        Assert.True((bool)value.GetType().GetProperty("keyAvailable")!.GetValue(value)!);
        Assert.Null(value.GetType().GetProperty("unavailableReason")!.GetValue(value));
    }

    [Fact]
    public async Task Connect_RejectsNonWebSocketAndMissingCaller()
    {
        await using var context = CreateContext();
        var controller = CreateController(context, configured: false);
        var badRequest = Assert.IsType<ObjectResult>(
            await controller.Connect(Guid.NewGuid(), cancellationToken: CancellationToken.None));
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);

        controller.HttpContext.Features.Set<IHttpWebSocketFeature>(new WebSocketFeature());
        var forbidden = await controller.Connect(Guid.NewGuid(), cancellationToken: CancellationToken.None);
        Assert.IsType<ForbidResult>(forbidden);
    }

    private static JitTerminalController CreateController(
        AuthorizationDbContext context,
        bool configured,
        JitSshOptions? options = null)
    {
        var broker = new Mock<IJitTerminalBroker>();
        broker.SetupGet(item => item.IsConfigured).Returns(configured);
        var controller = new JitTerminalController(
            context,
            broker.Object,
            Options.Create(options ?? new JitSshOptions()),
            NullLogger<JitTerminalController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return controller;
    }

    private static AuthorizationDbContext CreateContext(params TemporaryPermission[] permissions)
    {
        var context = new AuthorizationDbContext(new DbContextOptionsBuilder<AuthorizationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.TemporaryPermissions.AddRange(permissions);
        context.SaveChanges();
        return context;
    }

    private sealed class WebSocketFeature : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;
        public Task<System.Net.WebSockets.WebSocket> AcceptAsync(WebSocketAcceptContext context) =>
            throw new NotSupportedException();
    }
}
