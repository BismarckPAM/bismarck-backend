using AuthorizationService.Controllers;
using AuthorizationService.Data;
using AuthorizationService.DTOs;
using AuthorizationService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AuthorizationService.Tests;

public sealed class AccessPoliciesControllerCoverageTests
{
    [Fact]
    public async Task CreateGetUpdateAndDelete_ManagePolicyLifecycle()
    {
        await using var context = CreateContext();
        var controller = new AccessPoliciesController(context);
        var request = new AccessPolicyRequest
        {
            Role = " developer ", ResourceType = " vm ", Environment = " dev ",
            Criticality = " low ", MaxAccessLevel = 3, RequiresApprovalForElevated = true
        };

        var created = Assert.IsType<CreatedAtActionResult>(
            (await controller.Create(request, CancellationToken.None)).Result);
        var response = Assert.IsType<AccessPolicyResponse>(created.Value);
        Assert.Equal("DEVELOPER", response.Role);
        var all = Assert.IsType<OkObjectResult>(
            (await controller.GetAll(CancellationToken.None)).Result);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<AccessPolicyResponse>>(all.Value));

        var updated = Assert.IsType<OkObjectResult>((await controller.Update(
            response.Id,
            new AccessPolicyRequest
            {
                Role = "Admin", ResourceType = "Server", Environment = "Production",
                Criticality = "Critical", MaxAccessLevel = 5
            },
            CancellationToken.None)).Result);
        Assert.Equal("ADMIN", Assert.IsType<AccessPolicyResponse>(updated.Value).Role);

        var deleted = Assert.IsType<OkObjectResult>(
            (await controller.Delete(response.Id, CancellationToken.None)).Result);
        Assert.False(Assert.IsType<AccessPolicyResponse>(deleted.Value).IsActive);
    }

    [Fact]
    public async Task CreateAndUpdate_ValidateInputAndDuplicatePolicies()
    {
        await using var context = CreateContext();
        var controller = new AccessPoliciesController(context);
        var invalid = Assert.IsType<BadRequestObjectResult>((await controller.Create(
            new AccessPolicyRequest { Role = " ", ResourceType = "VM", Environment = "DEV", Criticality = "LOW", MaxAccessLevel = 9 },
            CancellationToken.None)).Result);
        Assert.NotNull(invalid.Value);

        var request = new AccessPolicyRequest
        {
            Role = "Developer", ResourceType = "VM", Environment = "DEV",
            Criticality = "LOW", MaxAccessLevel = 2
        };
        var first = Assert.IsType<CreatedAtActionResult>((await controller.Create(request, CancellationToken.None)).Result);
        var policy = Assert.IsType<AccessPolicyResponse>(first.Value);
        Assert.IsType<BadRequestObjectResult>((await controller.Create(request, CancellationToken.None)).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.GetById(Guid.NewGuid(), CancellationToken.None)).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.Update(Guid.NewGuid(), request, CancellationToken.None)).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.Delete(Guid.NewGuid(), CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.Update(policy.Id, request, CancellationToken.None)).Result);
    }

    [Theory]
    [InlineData("", "VM", "DEV", "LOW", 1)]
    [InlineData("Role", "", "DEV", "LOW", 1)]
    [InlineData("Role", "VM", "", "LOW", 1)]
    [InlineData("Role", "VM", "DEV", "", 1)]
    [InlineData("Role", "VM", "DEV", "LOW", -1)]
    [InlineData("Role", "VM", "DEV", "LOW", 6)]
    public async Task Create_RejectsEachInvalidPolicyField(
        string role, string resourceType, string environment, string criticality, int maxAccessLevel)
    {
        await using var context = CreateContext();
        var controller = new AccessPoliciesController(context);

        var result = await controller.Create(new AccessPolicyRequest
        {
            Role = role,
            ResourceType = resourceType,
            Environment = environment,
            Criticality = criticality,
            MaxAccessLevel = maxAccessLevel
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_RejectsNullRequest()
    {
        await using var context = CreateContext();
        var controller = new AccessPoliciesController(context);

        var result = await controller.Create(null!, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    private static AuthorizationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AuthorizationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
