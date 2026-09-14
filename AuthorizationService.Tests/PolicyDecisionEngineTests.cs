using System.Net;
using System.Net.Http;
using AuthorizationService.Clients;
using AuthorizationService.Controllers;
using AuthorizationService.Data;
using AuthorizationService.DTOs;
using AuthorizationService.Models;
using AuthorizationService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AuthorizationService.Tests;

public sealed class PolicyDecisionEngineTests
{
    [Fact]
    public async Task EvaluateAsync_DeveloperAccessingDevelopmentVm_Allows()
    {
        await using var context = CreateContext(new AccessPolicy
        {
            Role = "Developer",
            Environment = "Development",
            Criticality = "LOW",
            MaxAccessLevel = 3
        });
        var result = await CreateEngine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "Developer", true),
            new ResourceDto("VM", "Development", "LOW"),
            "SSH_ACCESS");

        Assert.Equal(AuthorizationDecision.ALLOW, result.Decision);
        Assert.Equal("AUTHORIZED", result.Reason);
        Assert.NotNull(result.ExpiresAt);
        Assert.Equal(ApprovalRequirement.NONE, result.ApprovalRequirement);
    }

    [Fact]
    public async Task EvaluateAsync_RootOnProductionCriticalResource_RequiresDualApproval()
    {
        await using var context = CreateContext(new AccessPolicy
        {
            Role = "DevOps",
            Environment = "Production",
            Criticality = "CRITICAL",
            MaxAccessLevel = 5
        });
        var result = await CreateEngine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "DevOps", true),
            new ResourceDto("Server", "Production", "CRITICAL"),
            "ROOT");

        Assert.Equal(AuthorizationDecision.APPROVAL_REQUIRED, result.Decision);
        Assert.Equal("ELEVATED_PRIVILEGE_ON_CRITICAL_RESOURCE", result.Reason);
        Assert.Equal(
            ApprovalRequirement.MANUAL_OR_AUTOMATED_DUAL_APPROVAL,
            result.ApprovalRequirement);
        Assert.Null(result.ExpiresAt);
    }

    [Fact]
    public async Task EvaluateAsync_DeveloperWriteOnProduction_RequiresApproval()
    {
        await using var context = CreateContext(new AccessPolicy
        {
            Role = "Developer",
            Environment = "PROD",
            Criticality = "LOW",
            MaxAccessLevel = 4
        });
        var result = await CreateEngine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "Developer", true),
            new ResourceDto("VM", "PROD", "LOW"),
            "DEPLOY_BUILD");

        Assert.Equal(AuthorizationDecision.APPROVAL_REQUIRED, result.Decision);
        Assert.Equal("PRODUCTION_ACCESS_REQUIRES_APPROVAL", result.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_DeactivatedUser_DeniesBeforePolicyLookup()
    {
        await using var context = CreateContext();
        var result = await CreateEngine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "Developer", false),
            new ResourceDto("VM", "Development", "LOW"),
            "SSH_ACCESS");

        AssertDenial(result, AuthorizationDenialReason.USER_DEACTIVATED);
    }

    [Fact]
    public async Task EvaluateAsync_MissingRole_Denies()
    {
        await using var context = CreateContext();
        var result = await CreateEngine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "", true),
            new ResourceDto("VM", "Development", "LOW"),
            "SSH_ACCESS");

        AssertDenial(result, AuthorizationDenialReason.USER_ROLE_NOT_ASSIGNED);
    }

    [Fact]
    public async Task EvaluateAsync_UnknownAction_Denies()
    {
        await using var context = CreateContext();
        var result = await CreateEngine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "Developer", true),
            new ResourceDto("VM", "Development", "LOW"),
            "FORMAT_DISK");

        AssertDenial(result, AuthorizationDenialReason.UNKNOWN_ACTION);
    }

    [Fact]
    public async Task EvaluateAsync_InsufficientRolePermission_Denies()
    {
        await using var context = CreateContext(new AccessPolicy
        {
            Role = "Developer",
            Environment = "Development",
            Criticality = "LOW",
            MaxAccessLevel = 2
        });
        var result = await CreateEngine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "Developer", true),
            new ResourceDto("VM", "Development", "LOW"),
            "SSH_ACCESS");

        AssertDenial(result, AuthorizationDenialReason.INSUFFICIENT_ROLE_PERMISSIONS);
    }

    [Fact]
    public async Task EvaluateAsync_NoMatchingPolicy_Denies()
    {
        await using var context = CreateContext();
        var result = await CreateEngine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "Developer", true),
            new ResourceDto("VM", "Development", "LOW"),
            "READ_STATUS");

        AssertDenial(result, AuthorizationDenialReason.INSUFFICIENT_ROLE_PERMISSIONS);
    }

    private static PolicyDecisionEngine CreateEngine(AuthorizationDbContext context) =>
        new(context);

    private static AuthorizationDbContext CreateContext(params AccessPolicy[] policies)
    {
        var options = new DbContextOptionsBuilder<AuthorizationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new AuthorizationDbContext(options);
        context.AccessPolicies.AddRange(policies);
        context.SaveChanges();
        return context;
    }

    private static void AssertDenial(
        AuthorizationDecisionResult result,
        AuthorizationDenialReason reason)
    {
        Assert.Equal(AuthorizationDecision.DENY, result.Decision);
        Assert.Equal(reason.ToString(), result.Reason);
        Assert.False(string.IsNullOrWhiteSpace(result.Details));
        Assert.Equal(ApprovalRequirement.NONE, result.ApprovalRequirement);
    }
}

public sealed class AuthorizationControllerTests
{
    [Fact]
    public async Task Check_WithValidRequest_ReturnsAllowWithRequestedSessionExpiry()
    {
        var userId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var identity = new Mock<IIdentityServiceClient>();
        var resource = new Mock<IResourceServiceClient>();
        var engine = new Mock<IPolicyDecisionEngine>();
        identity.Setup(client => client.GetUserRoleAsync(userId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<IdentityUser>(
                new IdentityUser(userId, Guid.NewGuid(), "Developer", true)));
        resource.Setup(client => client.GetResourceContextAsync(resourceId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<ResourceContext>(
                new ResourceContext("VM", "Development", "LOW")));
        engine.Setup(client => client.EvaluateAsync(
                It.IsAny<UserDto>(), It.IsAny<ResourceDto>(), "SSH_ACCESS", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizationDecisionResult.Allow(TimeSpan.FromMinutes(1)));

        var result = await CreateController(identity, resource, engine).Check(
            new AuthorizationCheckRequest(userId, resourceId, "SSH_ACCESS", 15),
            CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(response.Value);
        Assert.Equal(AuthorizationDecision.ALLOW, decision.Decision);
        Assert.InRange(
            decision.ExpiresAt!.Value,
            DateTimeOffset.UtcNow.AddMinutes(14),
            DateTimeOffset.UtcNow.AddMinutes(16));
        identity.VerifyAll();
        resource.VerifyAll();
        engine.VerifyAll();
    }

    [Fact]
    public async Task Check_WhenIdentityTimesOut_ReturnsFailClosedDenial()
    {
        var identity = new Mock<IIdentityServiceClient>();
        var resource = new Mock<IResourceServiceClient>();
        var engine = new Mock<IPolicyDecisionEngine>();
        identity.Setup(client => client.GetUserRoleAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<IdentityUser>(
                null, AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED));
        resource.Setup(client => client.GetResourceContextAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<ResourceContext>(
                new ResourceContext("VM", "Development", "LOW")));

        var result = await CreateController(identity, resource, engine).Check(
            new AuthorizationCheckRequest(Guid.NewGuid(), Guid.NewGuid(), "SSH_ACCESS"),
            CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(response.Value);
        AssertDenial(decision, AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED);
        engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Check_WhenResourceIsUnavailable_ReturnsFailClosedDenial()
    {
        var identity = new Mock<IIdentityServiceClient>();
        var resource = new Mock<IResourceServiceClient>();
        var engine = new Mock<IPolicyDecisionEngine>();
        identity.Setup(client => client.GetUserRoleAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<IdentityUser>(
                new IdentityUser(Guid.NewGuid(), Guid.NewGuid(), "Developer", true)));
        resource.Setup(client => client.GetResourceContextAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<ResourceContext>(
                null, AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED));

        var result = await CreateController(identity, resource, engine).Check(
            new AuthorizationCheckRequest(Guid.NewGuid(), Guid.NewGuid(), "SSH_ACCESS"),
            CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(response.Value);
        AssertDenial(decision, AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED);
        engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Check_WhenResourceIsNotFound_ReturnsResourceNotFound()
    {
        var identity = new Mock<IIdentityServiceClient>();
        var resource = new Mock<IResourceServiceClient>();
        var engine = new Mock<IPolicyDecisionEngine>();
        identity.Setup(client => client.GetUserRoleAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<IdentityUser>(
                new IdentityUser(Guid.NewGuid(), Guid.NewGuid(), "Developer", true)));
        resource.Setup(client => client.GetResourceContextAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<ResourceContext>(
                null, AuthorizationDenialReason.RESOURCE_NOT_FOUND));

        var result = await CreateController(identity, resource, engine).Check(
            new AuthorizationCheckRequest(Guid.NewGuid(), Guid.NewGuid(), "SSH_ACCESS"),
            CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(response.Value);
        AssertDenial(decision, AuthorizationDenialReason.RESOURCE_NOT_FOUND);
        engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Check_WhenUserIsDeactivated_ReturnsUserDeactivated()
    {
        var identity = new Mock<IIdentityServiceClient>();
        var resource = new Mock<IResourceServiceClient>();
        var engine = new Mock<IPolicyDecisionEngine>();
        identity.Setup(client => client.GetUserRoleAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<IdentityUser>(
                new IdentityUser(Guid.NewGuid(), Guid.NewGuid(), "Developer", false)));
        resource.Setup(client => client.GetResourceContextAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<ResourceContext>(
                new ResourceContext("VM", "Development", "LOW")));

        var result = await CreateController(identity, resource, engine).Check(
            new AuthorizationCheckRequest(Guid.NewGuid(), Guid.NewGuid(), "SSH_ACCESS"),
            CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(response.Value);
        AssertDenial(decision, AuthorizationDenialReason.USER_DEACTIVATED);
        engine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Check_WhenUserHasNoRole_ReturnsRoleNotAssigned()
    {
        var identity = new Mock<IIdentityServiceClient>();
        var resource = new Mock<IResourceServiceClient>();
        var engine = new Mock<IPolicyDecisionEngine>();
        identity.Setup(client => client.GetUserRoleAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<IdentityUser>(
                new IdentityUser(Guid.NewGuid(), Guid.NewGuid(), "", true)));
        resource.Setup(client => client.GetResourceContextAsync(
                It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceLookupResult<ResourceContext>(
                new ResourceContext("VM", "Development", "LOW")));

        var result = await CreateController(identity, resource, engine).Check(
            new AuthorizationCheckRequest(Guid.NewGuid(), Guid.NewGuid(), "SSH_ACCESS"),
            CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var decision = Assert.IsType<AuthorizationDecisionResult>(response.Value);
        AssertDenial(decision, AuthorizationDenialReason.USER_ROLE_NOT_ASSIGNED);
        engine.VerifyNoOtherCalls();
    }

    private static AuthorizationController CreateController(
        Mock<IIdentityServiceClient> identity,
        Mock<IResourceServiceClient> resource,
        Mock<IPolicyDecisionEngine> engine)
    {
        return new AuthorizationController(
            identity.Object,
            resource.Object,
            engine.Object);
    }

    private static void AssertDenial(
        AuthorizationDecisionResult result,
        AuthorizationDenialReason reason)
    {
        Assert.Equal(AuthorizationDecision.DENY, result.Decision);
        Assert.Equal(reason.ToString(), result.Reason);
        Assert.False(string.IsNullOrWhiteSpace(result.Details));
    }
}

public sealed class AuthorizationClientTests
{
    [Fact]
    public async Task IdentityClient_WhenRequestTimesOut_FailsClosed()
    {
        using var httpClient = new HttpClient(new ThrowingHandler(new TaskCanceledException()))
        {
            BaseAddress = new Uri("https://identity.test/")
        };
        var client = new IdentityServiceClient(
            httpClient,
            NullLogger<IdentityServiceClient>.Instance,
            new HttpContextAccessor());

        var result = await client.GetUserRoleAsync(Guid.NewGuid());

        Assert.Null(result.Value);
        Assert.Equal(AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED, result.FailureReason);
    }

    [Fact]
    public async Task ResourceClient_WhenUpstreamReturnsError_FailsClosed()
    {
        using var httpClient = new HttpClient(new ThrowingHandler(
            new HttpRequestException("hidden infrastructure detail")))
        {
            BaseAddress = new Uri("https://resource.test/")
        };
        var client = new ResourceServiceClient(
            httpClient,
            NullLogger<ResourceServiceClient>.Instance,
            new HttpContextAccessor());

        var result = await client.GetResourceContextAsync(Guid.NewGuid());

        Assert.Null(result.Value);
        Assert.Equal(AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED, result.FailureReason);
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }
}
