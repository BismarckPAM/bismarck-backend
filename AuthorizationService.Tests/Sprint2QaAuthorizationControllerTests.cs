using AuthorizationService.Clients;
using AuthorizationService.Controllers;
using AuthorizationService.DTOs;
using AuthorizationService.Models;
using AuthorizationService.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace AuthorizationService.Tests;

/// <summary>
/// Sprint 2 QA unit tests for BIS-204 fail-closed integration behavior
/// and BIS-208 security edge cases at controller level.
/// </summary>
public sealed class Sprint2QaAuthorizationControllerTests
{
    [Fact]
    public async Task Check_ValidUserAndResource_PassesMappedContextToEngine()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var identity = new Mock<IIdentityServiceClient>(MockBehavior.Strict);
        var resource = new Mock<IResourceServiceClient>(MockBehavior.Strict);
        var engine = new Mock<IPolicyDecisionEngine>(MockBehavior.Strict);

        identity.Setup(x => x.GetUserRoleAsync(userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<IdentityUser>(
                new IdentityUser(userId, roleId, "Developer", true)));
        resource.Setup(x => x.GetResourceContextAsync(resourceId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<ResourceContext>(
                new ResourceContext("VM", "DEV", "LOW")));
        engine.Setup(x => x.EvaluateAsync(
                It.Is<UserDto>(u => u.Id == userId && u.Role == "Developer" && u.IsActive),
                It.Is<ResourceDto>(r => r.Type == "VM" && r.Environment == "DEV" && r.Criticality == "LOW"),
                "SSH_ACCESS",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizationDecisionResult.Allow(TimeSpan.FromMinutes(1)));

        var result = await Controller(identity, resource, engine).Check(
            new AuthorizationCheckRequest(userId, resourceId, "SSH_ACCESS", 120),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(ok.Value);
        Assert.Equal(AuthorizationDecision.ALLOW, decision.Decision);
        identity.VerifyAll();
        resource.VerifyAll();
        engine.VerifyAll();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(120)]
    [InlineData(1440)]
    public async Task Check_Allow_UsesRequestedSessionDuration(int minutes)
    {
        var before = DateTimeOffset.UtcNow;
        var identity = ValidIdentity();
        var resource = ValidResource();
        var engine = new Mock<IPolicyDecisionEngine>();
        engine.Setup(x => x.EvaluateAsync(
                It.IsAny<UserDto>(),
                It.IsAny<ResourceDto>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizationDecisionResult.Allow(TimeSpan.FromMinutes(1)));

        var result = await Controller(identity, resource, engine).Check(
            new AuthorizationCheckRequest(Guid.NewGuid(), Guid.NewGuid(), "READ_STATUS", minutes),
            CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(ok.Value);
        Assert.NotNull(decision.ExpiresAt);
        Assert.InRange(
            decision.ExpiresAt!.Value,
            before.AddMinutes(minutes),
            after.AddMinutes(minutes).AddSeconds(1));
    }

    [Fact]
    public async Task Check_UnknownUser_ReturnsUserNotFound_AndSkipsEngine()
    {
        var identity = new Mock<IIdentityServiceClient>();
        var resource = ValidResource();
        var engine = new Mock<IPolicyDecisionEngine>();
        identity.Setup(x => x.GetUserRoleAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<IdentityUser>(
                null, AuthorizationDenialReason.USER_NOT_FOUND));

        var result = await Controller(identity, resource, engine).Check(
            new AuthorizationCheckRequest(Guid.NewGuid(), Guid.NewGuid(), "SSH_ACCESS"),
            CancellationToken.None);

        AssertDecision(result, AuthorizationDenialReason.USER_NOT_FOUND);
        engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Check_IdentityFailureWithoutReason_DefaultsToSystemFailClosed()
    {
        var identity = new Mock<IIdentityServiceClient>();
        var resource = ValidResource();
        var engine = new Mock<IPolicyDecisionEngine>();
        identity.Setup(x => x.GetUserRoleAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<IdentityUser>(null));

        var result = await Controller(identity, resource, engine).Check(
            new AuthorizationCheckRequest(Guid.NewGuid(), Guid.NewGuid(), "SSH_ACCESS"),
            CancellationToken.None);

        AssertDecision(result, AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED);
        engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Check_DeactivatedUser_ReturnsUserDeactivated_AndSkipsEngine()
    {
        var identity = new Mock<IIdentityServiceClient>();
        var resource = ValidResource();
        var engine = new Mock<IPolicyDecisionEngine>();
        identity.Setup(x => x.GetUserRoleAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<IdentityUser>(
                new IdentityUser(Guid.NewGuid(), Guid.NewGuid(), "Developer", false)));

        var result = await Controller(identity, resource, engine).Check(
            new AuthorizationCheckRequest(Guid.NewGuid(), Guid.NewGuid(), "SSH_ACCESS"),
            CancellationToken.None);

        AssertDecision(result, AuthorizationDenialReason.USER_DEACTIVATED);
        engine.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Check_UserWithoutRole_ReturnsRoleNotAssigned_AndSkipsEngine(string role)
    {
        var identity = new Mock<IIdentityServiceClient>();
        var resource = ValidResource();
        var engine = new Mock<IPolicyDecisionEngine>();
        identity.Setup(x => x.GetUserRoleAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<IdentityUser>(
                new IdentityUser(Guid.NewGuid(), Guid.NewGuid(), role, true)));

        var result = await Controller(identity, resource, engine).Check(
            new AuthorizationCheckRequest(Guid.NewGuid(), Guid.NewGuid(), "SSH_ACCESS"),
            CancellationToken.None);

        AssertDecision(result, AuthorizationDenialReason.USER_ROLE_NOT_ASSIGNED);
        engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Check_UnknownResource_ReturnsResourceNotFound_AndSkipsEngine()
    {
        var identity = ValidIdentity();
        var resource = new Mock<IResourceServiceClient>();
        var engine = new Mock<IPolicyDecisionEngine>();
        resource.Setup(x => x.GetResourceContextAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<ResourceContext>(
                null, AuthorizationDenialReason.RESOURCE_NOT_FOUND));

        var result = await Controller(identity, resource, engine).Check(
            new AuthorizationCheckRequest(Guid.NewGuid(), Guid.NewGuid(), "SSH_ACCESS"),
            CancellationToken.None);

        AssertDecision(result, AuthorizationDenialReason.RESOURCE_NOT_FOUND);
        engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Check_ResourceFailureWithoutReason_DefaultsToSystemFailClosed()
    {
        var identity = ValidIdentity();
        var resource = new Mock<IResourceServiceClient>();
        var engine = new Mock<IPolicyDecisionEngine>();
        resource.Setup(x => x.GetResourceContextAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<ResourceContext>(null));

        var result = await Controller(identity, resource, engine).Check(
            new AuthorizationCheckRequest(Guid.NewGuid(), Guid.NewGuid(), "SSH_ACCESS"),
            CancellationToken.None);

        AssertDecision(result, AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED);
        engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Check_EngineDeny_ReturnsDenyWithoutAddingExpiry()
    {
        var identity = ValidIdentity();
        var resource = ValidResource();
        var engine = new Mock<IPolicyDecisionEngine>();
        engine.Setup(x => x.EvaluateAsync(
                It.IsAny<UserDto>(),
                It.IsAny<ResourceDto>(),
                "ROOT_ACCESS",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizationDecisionResult.Deny(
                AuthorizationDenialReason.INSUFFICIENT_ROLE_PERMISSIONS));

        var result = await Controller(identity, resource, engine).Check(
            new AuthorizationCheckRequest(Guid.NewGuid(), Guid.NewGuid(), "ROOT_ACCESS", 120),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(ok.Value);
        Assert.Equal(AuthorizationDecision.DENY, decision.Decision);
        Assert.Null(decision.ExpiresAt);
    }

    [Fact]
    public async Task Check_EngineApprovalRequired_ReturnsApprovalUnchanged()
    {
        var identity = ValidIdentity();
        var resource = ValidResource();
        var engine = new Mock<IPolicyDecisionEngine>();
        engine.Setup(x => x.EvaluateAsync(
                It.IsAny<UserDto>(),
                It.IsAny<ResourceDto>(),
                "CONFIG_WRITE",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizationDecisionResult.ApprovalRequired("TEST_APPROVAL"));

        var result = await Controller(identity, resource, engine).Check(
            new AuthorizationCheckRequest(Guid.NewGuid(), Guid.NewGuid(), "CONFIG_WRITE", 120),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(ok.Value);
        Assert.Equal(AuthorizationDecision.APPROVAL_REQUIRED, decision.Decision);
        Assert.Equal("TEST_APPROVAL", decision.Reason);
        Assert.Null(decision.ExpiresAt);
    }

    private static Mock<IIdentityServiceClient> ValidIdentity()
    {
        var identity = new Mock<IIdentityServiceClient>();
        identity.Setup(x => x.GetUserRoleAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<IdentityUser>(
                new IdentityUser(Guid.NewGuid(), Guid.NewGuid(), "Developer", true)));
        return identity;
    }

    private static Mock<IResourceServiceClient> ValidResource()
    {
        var resource = new Mock<IResourceServiceClient>();
        resource.Setup(x => x.GetResourceContextAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<ResourceContext>(
                new ResourceContext("VM", "DEV", "LOW")));
        return resource;
    }

    private static AuthorizationController Controller(
        Mock<IIdentityServiceClient> identity,
        Mock<IResourceServiceClient> resource,
        Mock<IPolicyDecisionEngine> engine) =>
        new(identity.Object, resource.Object, engine.Object);

    private static void AssertDecision(
        ActionResult<AuthorizationDecisionResult> actionResult,
        AuthorizationDenialReason reason)
    {
        var ok = Assert.IsType<OkObjectResult>(actionResult.Result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(ok.Value);
        Assert.Equal(AuthorizationDecision.DENY, decision.Decision);
        Assert.Equal(reason.ToString(), decision.Reason);
        Assert.False(string.IsNullOrWhiteSpace(decision.Details));
    }
}
