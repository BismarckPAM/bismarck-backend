using System.Security.Claims;
using AuthorizationService.Controllers;
using AuthorizationService.Data;
using AuthorizationService.DTOs;
using AuthorizationService.Models;
using AuthorizationService.Services;
using Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AuthorizationService.Tests;

/// <summary>
/// BIS-405: Admin Dashboard privileges over JIT sessions.
///
/// Admin OR Security Admin may enumerate every session and revoke any of them;
/// an ordinary user is still confined to their own sessions and cannot revoke.
/// </summary>
public sealed class JitSessionsAdminTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RegularUser_SeesOnlyOwnSessions()
    {
        var callerId = Guid.NewGuid();
        await using var fixture = CreateFixture();
        fixture.AddPermission(callerId, "mine@x.io", TemporaryPermissionStatus.ACTIVE);
        fixture.AddPermission(Guid.NewGuid(), "theirs@x.io", TemporaryPermissionStatus.ACTIVE);

        var controller = CreateController(fixture, "Developer", callerId);
        var result = await controller.GetSessions(false, CancellationToken.None);

        var list = Sessions(result);
        Assert.Single(list);
        Assert.Equal("mine@x.io", list[0].UserEmail);
    }

    [Fact]
    public async Task Admin_SeesAllSessions()
    {
        await using var fixture = CreateFixture();
        fixture.AddPermission(Guid.NewGuid(), "a@x.io", TemporaryPermissionStatus.ACTIVE);
        fixture.AddPermission(Guid.NewGuid(), "b@x.io", TemporaryPermissionStatus.ACTIVE);

        var result = await CreateController(fixture, "Admin").GetSessions(false, CancellationToken.None);

        Assert.Equal(2, Sessions(result).Count);
    }

    [Fact]
    public async Task SecurityAdmin_SeesAllSessions()
    {
        await using var fixture = CreateFixture();
        fixture.AddPermission(Guid.NewGuid(), "a@x.io", TemporaryPermissionStatus.ACTIVE);
        fixture.AddPermission(Guid.NewGuid(), "b@x.io", TemporaryPermissionStatus.ACTIVE);

        var result = await CreateController(fixture, "Security Admin")
            .GetSessions(false, CancellationToken.None);

        Assert.Equal(2, Sessions(result).Count);
    }

    [Fact]
    public async Task ActiveOnly_FiltersServerSide()
    {
        await using var fixture = CreateFixture();
        fixture.AddPermission(Guid.NewGuid(), "live@x.io", TemporaryPermissionStatus.ACTIVE);
        fixture.AddPermission(Guid.NewGuid(), "dead@x.io", TemporaryPermissionStatus.REVOKED);

        var result = await CreateController(fixture, "Admin").GetSessions(true, CancellationToken.None);

        var list = Sessions(result);
        Assert.Single(list);
        Assert.Equal("live@x.io", list[0].UserEmail);
    }

    [Fact]
    public async Task RegularUser_RevokeIsForbidden()
    {
        await using var fixture = CreateFixture();
        var permission = fixture.AddPermission(Guid.NewGuid(), "a@x.io", TemporaryPermissionStatus.ACTIVE);

        var result = await CreateController(fixture, "Developer")
            .Revoke(permission.Id, null, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Security Admin")]
    public async Task AdminOrSecurityAdmin_RevokeSucceeds_WithFullRevocationLifecycle(string role)
    {
        await using var fixture = CreateFixture();
        var permission = fixture.AddPermission(Guid.NewGuid(), "a@x.io", TemporaryPermissionStatus.ACTIVE);

        var result = await CreateController(fixture, role).Revoke(
            permission.Id, new RevokeJitSessionRequest("Suspicious"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);

        var saved = await fixture.Context.TemporaryPermissions.AsNoTracking().SingleAsync();
        Assert.Equal(TemporaryPermissionStatus.REVOKED, saved.Status);
        Assert.Equal(Now, saved.RevokedAt);
        Assert.Equal(fixture.ActorId, saved.RevokedByUserId);

        // Immediate effect: the live brokered terminal is dropped and the cloud
        // grant removal is attempted — not merely a status badge.
        fixture.TerminalBroker.Verify(
            broker => broker.CloseAsync(permission.Id, It.IsAny<string>()), Times.Once);
        fixture.Provisioner.Verify(
            provisioner => provisioner.RevokeAsync(
                It.IsAny<TemporaryPermission>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // And the revocation is published for audit.
        fixture.EventPublisher.Verify(publisher => publisher.PublishAsync(
            KafkaTopics.JitRevoked,
            It.Is<SecurityEvent<object>>(message =>
                message.Action == "MANUAL_REVOKE_JIT_SESSION" && message.Outcome == "SUCCESS"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revoke_UnknownPermission_ReturnsNotFound()
    {
        await using var fixture = CreateFixture();

        var result = await CreateController(fixture, "Security Admin")
            .Revoke(Guid.NewGuid(), null, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Revoke_AlreadyClosedSession_ReturnsConflict()
    {
        await using var fixture = CreateFixture();
        var permission = fixture.AddPermission(Guid.NewGuid(), "a@x.io", TemporaryPermissionStatus.REVOKED);

        var result = await CreateController(fixture, "Security Admin")
            .Revoke(permission.Id, null, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    /// <summary>
    /// Unwraps the controller's <c>ActionResult&lt;IReadOnlyList&lt;JitSessionResponse&gt;&gt;</c>
    /// to the session list. The controller returns <c>Ok(...)</c>, which binds to
    /// the wrapper's <c>Result</c> property (via the implicit conversion), while
    /// <c>Value</c> stays null — so both are checked.
    /// </summary>
    private static IReadOnlyList<JitSessionResponse> Sessions(object result)
    {
        var payload = result switch
        {
            ActionResult<IReadOnlyList<JitSessionResponse>> { Value: not null } action => action.Value,
            ActionResult<IReadOnlyList<JitSessionResponse>> { Result: OkObjectResult ok } => ok.Value,
            OkObjectResult direct => direct.Value,
            _ => result,
        };

        return Assert.IsAssignableFrom<IReadOnlyList<JitSessionResponse>>(payload)
            ?? throw new InvalidOperationException("No sessions returned.");
    }

    private static JitSessionsController CreateController(
        Fixture fixture,
        string role,
        Guid? callerId = null)
    {
        var actorId = callerId ?? fixture.ActorId;
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, actorId.ToString()),
                    new Claim(ClaimTypes.Role, role)
                ],
                "Test"))
        };
        var controller = new JitSessionsController(
            fixture.Context,
            fixture.Provisioner.Object,
            fixture.TerminalBroker.Object,
            fixture.EventPublisher.Object,
            new TestClock(Now));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static Fixture CreateFixture()
    {
        var options = new DbContextOptionsBuilder<AuthorizationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new Fixture(new AuthorizationDbContext(options));
    }

    private sealed class Fixture(AuthorizationDbContext context) : IAsyncDisposable
    {
        public Guid ActorId { get; } = Guid.NewGuid();
        public AuthorizationDbContext Context { get; } = context;
        public Mock<IAzureJitProvisioner> Provisioner { get; } = new();
        public Mock<IJitTerminalBroker> TerminalBroker { get; } = new();
        public Mock<IAuthorizationEventPublisher> EventPublisher { get; } = new();

        public TemporaryPermission AddPermission(
            Guid userId,
            string userEmail,
            TemporaryPermissionStatus status)
        {
            var permission = new TemporaryPermission
            {
                ApprovalId = Guid.NewGuid(),
                UserId = userId,
                UserEmail = userEmail,
                ResourceId = Guid.NewGuid(),
                ResourceName = "Prod DB",
                RequestedLevel = 3,
                GrantedAt = Now.AddMinutes(-10),
                ExpiresAt = Now.AddMinutes(50),
                Status = status
            };
            Context.TemporaryPermissions.Add(permission);
            Context.SaveChanges();
            return permission;
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class TestClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow => now;
    }
}