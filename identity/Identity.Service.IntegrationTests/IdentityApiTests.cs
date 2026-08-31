using System.Net;
using System.Net.Http.Json;
using Identity.Service.Data;
using Identity.Service.DTOs;
using Identity.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Identity.Service.IntegrationTests;

public class IdentityApiTests(IdentityApiFixture fixture) : IClassFixture<IdentityApiFixture>
{
    private readonly HttpClient client = fixture.CreateClient();

    [Fact]
    public async Task LoginReturnsTokenForActiveUser()
    {
        var response = await client.PostAsJsonAsync("/api/identity/auth/login", new LoginRequest
        {
            Email = fixture.AdminEmail, Password = fixture.AdminPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        Assert.False(string.IsNullOrWhiteSpace(login.Token));
        Assert.Equal(fixture.AdminId, login.User.Id);
        Assert.Equal(fixture.AdminEmail, login.User.Email);
        Assert.Equal("Admin", login.User.Role);
        Assert.Equal("Engineering", login.User.Department);
    }

    [Theory]
    [InlineData("admin@example.com", "wrong-password")]
    [InlineData("unknown@example.com", "AdminPassword123!")]
    public async Task LoginRejectsInvalidCredentialsGenerically(string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/identity/auth/login", new LoginRequest
        {
            Email = email, Password = password
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Invalid email or password", (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Message);
    }

    [Fact]
    public async Task LoginRejectsInactiveUserGenerically()
    {
        using (var scope = fixture.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            context.Users.Add(new User
            {
                Id = Guid.NewGuid(), FullName = "Inactive", Email = "inactive@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("InactivePassword123!"),
                RoleId = fixture.RoleId, DepartmentId = fixture.DepartmentId, IsActive = false
            });
            await context.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync("/api/identity/auth/login", new LoginRequest
        {
            Email = "inactive@example.com", Password = "InactivePassword123!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Invalid email or password", (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Message);
    }

    [Theory]
    [InlineData("", "AdminPassword123!")]
    [InlineData("admin@example.com", "")]
    public async Task LoginMissingFieldsCurrentlyReturnsUnauthorizedBecauseNoValidatorExists(string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/identity/auth/login", new LoginRequest
        {
            Email = email, Password = password
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Invalid email or password", (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Message);
    }

    [Fact]
    public async Task UsersRequireValidToken()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/identity/users")).StatusCode);

        using var authenticatedClient = await CreateAuthenticatedClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await authenticatedClient.GetAsync("/api/identity/users")).StatusCode);

        using var malformedClient = fixture.CreateClient();
        malformedClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-valid-jwt");
        Assert.Equal(HttpStatusCode.Unauthorized, (await malformedClient.GetAsync("/api/identity/users")).StatusCode);
    }

    [Fact]
    public async Task UserLifecycleReturnsExpectedResponsesAndSoftDeletes()
    {
        using var authenticatedClient = await CreateAuthenticatedClientAsync();
        var request = new CreateUserRequest
        {
            FullName = "Integration Person", Email = "integration@example.com",
            Password = "IntegrationPassword123!",
            RoleId = fixture.RoleId, DepartmentId = fixture.DepartmentId
        };

        var create = await authenticatedClient.PostAsJsonAsync("/api/identity/users", request);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.NotNull(create.Headers.Location);
        var user = await create.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);

        Assert.Equal(HttpStatusCode.OK, (await authenticatedClient.GetAsync("/api/identity/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await authenticatedClient.GetAsync($"/api/identity/users/{user.Id}")).StatusCode);

        request.FullName = "Updated Integration Person";
        var update = await authenticatedClient.PutAsJsonAsync($"/api/identity/users/{user.Id}", new UpdateUserRequest
        {
            FullName = request.FullName, Email = request.Email, RoleId = request.RoleId,
            DepartmentId = request.DepartmentId, IsActive = true
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var delete = await authenticatedClient.DeleteAsync($"/api/identity/users/{user.Id}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.False((await delete.Content.ReadFromJsonAsync<UserResponse>())!.IsActive);
        Assert.Equal(HttpStatusCode.NotFound, (await authenticatedClient.GetAsync($"/api/identity/users/{user.Id}")).StatusCode);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False((await context.Users.IgnoreQueryFilters().SingleAsync(item => item.Id == user.Id)).IsActive);
    }

    [Fact]
    public async Task InvalidRoleAndDuplicateEmailReturnStructuredBadRequest()
    {
        using var authenticatedClient = await CreateAuthenticatedClientAsync();
        var missingRole = await authenticatedClient.PostAsJsonAsync("/api/identity/users", new CreateUserRequest
        {
            FullName = "Invalid", Email = "invalid@example.com", Password = "InvalidPassword123!", RoleId = Guid.Empty, DepartmentId = fixture.DepartmentId
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingRole.StatusCode);
        Assert.Contains("errors", await missingRole.Content.ReadAsStringAsync());

        var first = await authenticatedClient.PostAsJsonAsync("/api/identity/users", new CreateUserRequest
        {
            FullName = "First", Email = "duplicate@example.com", Password = "DuplicatePassword123!", RoleId = fixture.RoleId, DepartmentId = fixture.DepartmentId
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var duplicate = await authenticatedClient.PostAsJsonAsync("/api/identity/users", new CreateUserRequest
        {
            FullName = "Second", Email = "DUPLICATE@example.com", Password = "DuplicatePassword123!", RoleId = fixture.RoleId, DepartmentId = fixture.DepartmentId
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("errors", await duplicate.Content.ReadAsStringAsync());
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var response = await client.PostAsJsonAsync("/api/identity/auth/login", new LoginRequest
        {
            Email = fixture.AdminEmail, Password = fixture.AdminPassword
        });
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        var authenticatedClient = fixture.CreateClient();
        authenticatedClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", login!.Token);
        return authenticatedClient;
    }

    private sealed class ErrorResponse
    {
        public string Message { get; set; } = string.Empty;
    }
}
