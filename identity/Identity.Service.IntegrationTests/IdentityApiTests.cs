using System.Net;
using System.Net.Http.Json;
using Identity.Service.Data;
using Identity.Service.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Identity.Service.IntegrationTests;

public class IdentityApiTests(IdentityApiFixture fixture) : IClassFixture<IdentityApiFixture>
{
    private readonly HttpClient client = fixture.CreateClient();

    [Fact]
    public async Task UserLifecycleReturnsExpectedResponsesAndSoftDeletes()
    {
        var request = new CreateUserRequest
        {
            FullName = "Integration Person", Email = "integration@example.com",
            RoleId = fixture.RoleId, DepartmentId = fixture.DepartmentId
        };

        var create = await client.PostAsJsonAsync("/api/identity/users", request);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.NotNull(create.Headers.Location);
        var user = await create.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/identity/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/identity/users/{user.Id}")).StatusCode);

        request.FullName = "Updated Integration Person";
        var update = await client.PutAsJsonAsync($"/api/identity/users/{user.Id}", new UpdateUserRequest
        {
            FullName = request.FullName, Email = request.Email, RoleId = request.RoleId,
            DepartmentId = request.DepartmentId, IsActive = true
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var delete = await client.DeleteAsync($"/api/identity/users/{user.Id}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.False((await delete.Content.ReadFromJsonAsync<UserResponse>())!.IsActive);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/identity/users/{user.Id}")).StatusCode);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False((await context.Users.IgnoreQueryFilters().SingleAsync(item => item.Id == user.Id)).IsActive);
    }

    [Fact]
    public async Task InvalidRoleAndDuplicateEmailReturnStructuredBadRequest()
    {
        var missingRole = await client.PostAsJsonAsync("/api/identity/users", new CreateUserRequest
        {
            FullName = "Invalid", Email = "invalid@example.com", RoleId = Guid.Empty, DepartmentId = fixture.DepartmentId
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingRole.StatusCode);
        Assert.Contains("errors", await missingRole.Content.ReadAsStringAsync());

        var first = await client.PostAsJsonAsync("/api/identity/users", new CreateUserRequest
        {
            FullName = "First", Email = "duplicate@example.com", RoleId = fixture.RoleId, DepartmentId = fixture.DepartmentId
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var duplicate = await client.PostAsJsonAsync("/api/identity/users", new CreateUserRequest
        {
            FullName = "Second", Email = "DUPLICATE@example.com", RoleId = fixture.RoleId, DepartmentId = fixture.DepartmentId
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("errors", await duplicate.Content.ReadAsStringAsync());
    }
}
