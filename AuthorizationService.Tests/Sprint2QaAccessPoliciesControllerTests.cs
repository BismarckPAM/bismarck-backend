using AuthorizationService.Controllers;
using AuthorizationService.Data;
using AuthorizationService.DTOs;
using AuthorizationService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AuthorizationService.Tests;

/// <summary>
/// Sprint 2 QA unit tests for BIS-203 policy CRUD, normalization,
/// duplicate protection and soft deactivation.
/// </summary>
public sealed class Sprint2QaAccessPoliciesControllerTests
{
    [Fact]
    public async Task Create_ValidPolicy_Returns201AndPersistsNormalizedPolicy()
    {
        await using var context = CreateContext();
        var controller = new PoliciesController(context);

        var result = await controller.Create(
            Request(" developer ", " vm ", " dev ", " low ", 3, true),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(201, created.StatusCode);
        var response = Assert.IsType<PolicyResponse>(created.Value);
        Assert.Equal("DEVELOPER", response.Role);
        Assert.Equal("VM", response.ResourceType);
        Assert.Equal("DEV", response.Environment);
        Assert.Equal("LOW", response.Criticality);
        Assert.Equal(3, response.MaxAccessLevel);
        Assert.True(response.RequiresApprovalForElevated);
        Assert.True(response.IsActive);

        var stored = await context.AccessPolicies.SingleAsync();
        Assert.Equal("DEVELOPER", stored.Role);
        Assert.Equal("DEV", stored.Environment);
        Assert.Equal("LOW", stored.Criticality);
        Assert.Equal(3, stored.MaxAccessLevel);
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task Create_NullResourceType_DefaultsResponseToVm()
    {
        await using var context = CreateContext();
        var controller = new PoliciesController(context);

        var result = await controller.Create(
            new UpsertPolicyRequest("Developer", null, "Dev", "Low", 3, true),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<PolicyResponse>(created.Value);
        Assert.Equal("VM", response.ResourceType);
    }

    [Fact]
    public async Task Create_DuplicateRoleEnvironmentCriticality_Returns400()
    {
        await using var context = CreateContext(Policy("DEVELOPER", "DEV", "LOW", 3));
        var controller = new PoliciesController(context);

        var result = await controller.Create(
            Request("developer", "VM", "dev", "low", 5, false),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.NotNull(badRequest.Value);
        Assert.Equal(1, await context.AccessPolicies.CountAsync());
    }

    [Fact]
    public async Task Create_DuplicateDetection_DoesNotDependOnResourceType()
    {
        await using var context = CreateContext(Policy("DEVELOPER", "DEV", "LOW", 3));
        var controller = new PoliciesController(context);

        var result = await controller.Create(
            Request("DEVELOPER", "DATABASE", "DEV", "LOW", 4, true),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetAll_ReturnsActiveAndInactivePolicies()
    {
        await using var context = CreateContext(
            Policy("ADMIN", "DEV", "LOW", 5, true),
            Policy("VIEWER", "DEV", "LOW", 1, false));
        var controller = new PoliciesController(context);

        var result = await controller.GetAll(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var policies = Assert.IsAssignableFrom<IReadOnlyList<PolicyResponse>>(ok.Value);
        Assert.Equal(2, policies.Count);
        Assert.Contains(policies, p => p.Role == "ADMIN" && p.IsActive);
        Assert.Contains(policies, p => p.Role == "VIEWER" && !p.IsActive);
    }

    [Fact]
    public async Task GetAll_OrdersByRoleThenEnvironmentThenCriticality()
    {
        await using var context = CreateContext(
            Policy("VIEWER", "DEV", "LOW", 1),
            Policy("ADMIN", "PROD", "CRITICAL", 5),
            Policy("ADMIN", "DEV", "LOW", 5));
        var controller = new PoliciesController(context);

        var result = await controller.GetAll(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var policies = Assert.IsAssignableFrom<IReadOnlyList<PolicyResponse>>(ok.Value);
        Assert.Collection(
            policies,
            first => Assert.Equal(("ADMIN", "DEV", "LOW"), (first.Role, first.Environment, first.Criticality)),
            second => Assert.Equal(("ADMIN", "PROD", "CRITICAL"), (second.Role, second.Environment, second.Criticality)),
            third => Assert.Equal("VIEWER", third.Role));
    }

    [Fact]
    public async Task GetById_ExistingPolicy_Returns200()
    {
        var policy = Policy("ADMIN", "DEV", "LOW", 5);
        await using var context = CreateContext(policy);
        var controller = new PoliciesController(context);

        var result = await controller.GetById(policy.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PolicyResponse>(ok.Value);
        Assert.Equal(policy.Id, response.Id);
    }

    [Fact]
    public async Task GetById_UnknownPolicy_Returns404()
    {
        await using var context = CreateContext();
        var controller = new PoliciesController(context);

        var result = await controller.GetById(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetById_InactivePolicy_IsStillRetrievable()
    {
        var policy = Policy("VIEWER", "DEV", "LOW", 1, false);
        await using var context = CreateContext(policy);
        var controller = new PoliciesController(context);

        var result = await controller.GetById(policy.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PolicyResponse>(ok.Value);
        Assert.False(response.IsActive);
    }

    [Fact]
    public async Task Update_ExistingPolicy_Returns200AndPersistsChanges()
    {
        var policy = Policy("DEVELOPER", "DEV", "LOW", 3);
        await using var context = CreateContext(policy);
        var controller = new PoliciesController(context);

        var result = await controller.Update(
            policy.Id,
            Request(" developer ", "vm", " dev ", " low ", 4, false),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PolicyResponse>(ok.Value);
        Assert.Equal(4, response.MaxAccessLevel);
        Assert.False(response.RequiresApprovalForElevated);
        Assert.Equal("DEVELOPER", response.Role);
        Assert.True(response.IsActive);

        var stored = await context.AccessPolicies.SingleAsync();
        Assert.Equal(4, stored.MaxAccessLevel);
        Assert.False(stored.RequiresApprovalForElevated);
    }

    [Fact]
    public async Task Update_InactivePolicy_DoesNotReactivateIt()
    {
        var policy = Policy("DEVELOPER", "DEV", "LOW", 3, false);
        await using var context = CreateContext(policy);
        var controller = new PoliciesController(context);

        var result = await controller.Update(
            policy.Id,
            Request("Developer", "VM", "DEV", "LOW", 4, true),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PolicyResponse>(ok.Value);
        Assert.False(response.IsActive);
    }

    [Fact]
    public async Task Update_UnknownPolicy_Returns404()
    {
        await using var context = CreateContext();
        var controller = new PoliciesController(context);

        var result = await controller.Update(
            Guid.NewGuid(),
            Request("Developer", "VM", "DEV", "LOW", 3, true),
            CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Update_ToConflictingPolicyKey_Returns400()
    {
        var developer = Policy("DEVELOPER", "DEV", "LOW", 3);
        var admin = Policy("ADMIN", "DEV", "LOW", 5);
        await using var context = CreateContext(developer, admin);
        var controller = new PoliciesController(context);

        var result = await controller.Update(
            admin.Id,
            Request("Developer", "VM", "DEV", "LOW", 5, true),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Deactivate_ExistingPolicy_SoftDeletesWithoutRemovingRow()
    {
        var policy = Policy("DEVELOPER", "DEV", "LOW", 3);
        await using var context = CreateContext(policy);
        var controller = new PoliciesController(context);

        var result = await controller.Deactivate(policy.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PolicyResponse>(ok.Value);
        Assert.False(response.IsActive);
        Assert.Equal(1, await context.AccessPolicies.CountAsync());
        Assert.False((await context.AccessPolicies.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Deactivate_AlreadyInactivePolicy_RemainsInactive()
    {
        var policy = Policy("DEVELOPER", "DEV", "LOW", 3, false);
        await using var context = CreateContext(policy);
        var controller = new PoliciesController(context);

        var result = await controller.Deactivate(policy.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PolicyResponse>(ok.Value);
        Assert.False(response.IsActive);
    }

    [Fact]
    public async Task Deactivate_UnknownPolicy_Returns404()
    {
        await using var context = CreateContext();
        var controller = new PoliciesController(context);

        var result = await controller.Deactivate(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    private static UpsertPolicyRequest Request(
        string role,
        string? resourceType,
        string environment,
        string criticality,
        int maxAccessLevel,
        bool requiresApproval) =>
        new(role, resourceType, environment, criticality, maxAccessLevel, requiresApproval);

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
}
