using AuthorizationService.Data;
using AuthorizationService.Models;
using AuthorizationService.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AuthorizationService.Tests;

/// <summary>
/// Additional Sprint 2 QA tests for BIS-202/BIS-208 authorization decisions.
/// These tests are written against the exact current AuthorizationService source.
/// </summary>
public sealed class Sprint2QaPolicyDecisionEngineTests
{
    [Fact]
    public async Task Developer_DevVm_SshAccess_IsAllowed()
    {
        await using var context = CreateContext(Policy("DEVELOPER", "DEV", "LOW", 3));

        var result = await Engine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "Developer", true),
            new ResourceDto("VM", "DEV", "LOW"),
            "SSH_ACCESS");

        Assert.Equal(AuthorizationDecision.ALLOW, result.Decision);
        Assert.Equal("AUTHORIZED", result.Reason);
        Assert.NotNull(result.ExpiresAt);
        Assert.Equal(ApprovalRequirement.NONE, result.ApprovalRequirement);
    }

    [Fact]
    public async Task Matching_IsCaseInsensitive_ForRoleEnvironmentCriticalityAndAction()
    {
        await using var context = CreateContext(Policy("developer", "dev", "low", 3));

        var result = await Engine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "DEVELOPER", true),
            new ResourceDto("VM", "DEV", "LOW"),
            "ssh_access");

        Assert.Equal(AuthorizationDecision.ALLOW, result.Decision);
    }

    [Fact]
    public async Task UnknownAction_IsDenied()
    {
        await using var context = CreateContext(Policy("DEVELOPER", "DEV", "LOW", 5));

        var result = await Engine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "Developer", true),
            new ResourceDto("VM", "DEV", "LOW"),
            "NOT_A_REAL_ACTION");

        AssertDenied(result, AuthorizationDenialReason.UNKNOWN_ACTION);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankAction_IsDenied(string action)
    {
        await using var context = CreateContext(Policy("DEVELOPER", "DEV", "LOW", 5));

        var result = await Engine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "Developer", true),
            new ResourceDto("VM", "DEV", "LOW"),
            action);

        AssertDenied(result, AuthorizationDenialReason.UNKNOWN_ACTION);
    }

    [Fact]
    public async Task DeactivatedUser_IsDeniedBeforePolicyEvaluation()
    {
        await using var context = CreateContext(Policy("DEVELOPER", "DEV", "LOW", 5));

        var result = await Engine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "Developer", false),
            new ResourceDto("VM", "DEV", "LOW"),
            "READ_STATUS");

        AssertDenied(result, AuthorizationDenialReason.USER_DEACTIVATED);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingRole_IsDenied(string role)
    {
        await using var context = CreateContext();

        var result = await Engine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), role, true),
            new ResourceDto("VM", "DEV", "LOW"),
            "READ_STATUS");

        AssertDenied(result, AuthorizationDenialReason.USER_ROLE_NOT_ASSIGNED);
    }

    [Fact]
    public async Task NoMatchingPolicy_IsDenied()
    {
        await using var context = CreateContext(Policy("VIEWER", "DEV", "LOW", 1));

        var result = await Engine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "Developer", true),
            new ResourceDto("VM", "DEV", "LOW"),
            "READ_STATUS");

        AssertDenied(result, AuthorizationDenialReason.INSUFFICIENT_ROLE_PERMISSIONS);
    }

    [Fact]
    public async Task InactiveMatchingPolicy_IsIgnoredAndDenied()
    {
        await using var context = CreateContext(Policy("DEVELOPER", "DEV", "LOW", 5, isActive: false));

        var result = await Engine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "Developer", true),
            new ResourceDto("VM", "DEV", "LOW"),
            "READ_STATUS");

        AssertDenied(result, AuthorizationDenialReason.INSUFFICIENT_ROLE_PERMISSIONS);
    }

    [Fact]
    public async Task RequiredActionLevelAbovePolicyMax_IsDenied()
    {
        await using var context = CreateContext(Policy("DEVELOPER", "DEV", "LOW", 2));

        var result = await Engine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "Developer", true),
            new ResourceDto("VM", "DEV", "LOW"),
            "SSH_ACCESS");

        AssertDenied(result, AuthorizationDenialReason.INSUFFICIENT_ROLE_PERMISSIONS);
    }

    [Theory]
    [InlineData("VIEW_LOGS", 1)]
    [InlineData("READ_STATUS", 1)]
    [InlineData("EXECUTE_QUERY", 2)]
    [InlineData("APP_CONNECT", 2)]
    [InlineData("SSH_ACCESS", 3)]
    [InlineData("DEPLOY_BUILD", 3)]
    [InlineData("DB_MIGRATION", 4)]
    [InlineData("CONFIG_WRITE", 4)]
    [InlineData("ADMIN_WRITE", 4)]
    [InlineData("SCHEMA_UPDATE", 4)]
    [InlineData("ROOT_ACCESS", 5)]
    [InlineData("ROOT", 5)]
    [InlineData("DROP_TABLE", 5)]
    public async Task SupportedActions_AreAllowed_WhenPolicyLevelIsSufficient_OnDev(
        string action,
        int level)
    {
        await using var context = CreateContext(Policy("QA", "DEV", "LOW", level));

        var result = await Engine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "QA", true),
            new ResourceDto("VM", "DEV", "LOW"),
            action);

        Assert.Equal(AuthorizationDecision.ALLOW, result.Decision);
    }

    [Fact]
    public async Task Developer_ProdSsh_RequiresApproval_WhenPolicyAllowsLevelThree()
    {
        await using var context = CreateContext(Policy("DEVELOPER", "PROD", "LOW", 3));

        var result = await Engine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "Developer", true),
            new ResourceDto("VM", "PROD", "LOW"),
            "SSH_ACCESS");

        Assert.Equal(AuthorizationDecision.APPROVAL_REQUIRED, result.Decision);
        Assert.Equal("PRODUCTION_ACCESS_REQUIRES_APPROVAL", result.Reason);
        Assert.Equal(
            ApprovalRequirement.MANUAL_OR_AUTOMATED_DUAL_APPROVAL,
            result.ApprovalRequirement);
        Assert.Null(result.ExpiresAt);
    }

    [Fact]
    public async Task NonAdmin_ProdCriticalRoot_RequiresElevatedApproval_WhenPolicyAllowsLevelFive()
    {
        await using var context = CreateContext(Policy("DEVOPS", "PROD", "CRITICAL", 5));

        var result = await Engine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "DevOps", true),
            new ResourceDto("SERVER", "PROD", "CRITICAL"),
            "ROOT_ACCESS");

        Assert.Equal(AuthorizationDecision.APPROVAL_REQUIRED, result.Decision);
        Assert.Equal("ELEVATED_PRIVILEGE_ON_CRITICAL_RESOURCE", result.Reason);
        Assert.Equal(
            ApprovalRequirement.MANUAL_OR_AUTOMATED_DUAL_APPROVAL,
            result.ApprovalRequirement);
    }

    [Fact]
    public async Task Developer_ProdRoot_WithCurrentLevelThreePolicy_IsDeniedBeforeApprovalBranch()
    {
        await using var context = CreateContext(Policy("DEVELOPER", "PROD", "CRITICAL", 3));

        var result = await Engine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "Developer", true),
            new ResourceDto("VM", "PROD", "CRITICAL"),
            "ROOT_ACCESS");

        AssertDenied(result, AuthorizationDenialReason.INSUFFICIENT_ROLE_PERMISSIONS);
    }

    [Fact]
    public async Task Admin_ProdCriticalRoot_IsAllowed_ByCurrentImplementation()
    {
        await using var context = CreateContext(Policy("ADMIN", "PROD", "CRITICAL", 5));

        var result = await Engine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "Admin", true),
            new ResourceDto("VM", "PROD", "CRITICAL"),
            "ROOT_ACCESS");

        Assert.Equal(AuthorizationDecision.ALLOW, result.Decision);
        Assert.Equal("AUTHORIZED", result.Reason);
        Assert.NotNull(result.ExpiresAt);
    }

    [Fact]
    public async Task CriticalDev_LevelFour_NonAdmin_RequiresApproval()
    {
        await using var context = CreateContext(Policy("OPERATOR", "DEV", "CRITICAL", 4));

        var result = await Engine(context).EvaluateAsync(
            new UserDto(Guid.NewGuid(), "Operator", true),
            new ResourceDto("VM", "DEV", "CRITICAL"),
            "CONFIG_WRITE");

        Assert.Equal(AuthorizationDecision.APPROVAL_REQUIRED, result.Decision);
        Assert.Equal("ELEVATED_PRIVILEGE_ON_CRITICAL_RESOURCE", result.Reason);
    }

    private static PolicyDecisionEngine Engine(AuthorizationDbContext context) => new(context);

    private static AccessPolicy Policy(
        string role,
        string environment,
        string criticality,
        int maxAccessLevel,
        bool isActive = true) =>
        new()
        {
            Role = role,
            Environment = environment,
            Criticality = criticality,
            MaxAccessLevel = maxAccessLevel,
            RequiresApprovalForElevated = true,
            IsActive = isActive
        };

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

    private static void AssertDenied(
        AuthorizationDecisionResult result,
        AuthorizationDenialReason reason)
    {
        Assert.Equal(AuthorizationDecision.DENY, result.Decision);
        Assert.Equal(reason.ToString(), result.Reason);
        Assert.Equal(ApprovalRequirement.NONE, result.ApprovalRequirement);
        Assert.False(string.IsNullOrWhiteSpace(result.Details));
        Assert.Null(result.ExpiresAt);
    }
}
