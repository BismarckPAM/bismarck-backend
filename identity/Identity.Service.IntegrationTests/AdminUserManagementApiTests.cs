using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Identity.Service.Data;
using Identity.Service.DTOs;
using Identity.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Identity.Service.IntegrationTests;

/// <summary>
/// BIS-405: HTTP-level protection and behaviour of the Admin user-management
/// endpoints, including the exact 401 / 403 / 404 / 200 contract the Admin
/// Dashboard UI depends on.
/// </summary>
public class AdminUserManagementApiTests(IdentityApiFixture fixture) : IClassFixture<IdentityApiFixture>
{
    private const string AdminListPath = "/api/identity/users/admin/all";

    private readonly HttpClient client = fixture.CreateClient();

    // --- Authorization ---------------------------------------------------------

    [Fact]
    public async Task AdminList_WithoutToken_ReturnsUnauthorized()
    {
        var response = await client.GetAsync(AdminListPath);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminList_AsOrdinaryUser_ReturnsForbidden()
    {
        using var developer = await AuthenticatedAsync(fixture.DeveloperEmail, fixture.DeveloperPassword);
        var response = await developer.GetAsync(AdminListPath);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdminList_AsAdminOrSecurityAdmin_ReturnsOk(bool securityAdmin)
    {
        using var caller = securityAdmin
            ? await AuthenticatedAsync(fixture.SecurityAdminEmail, fixture.SecurityAdminPassword)
            : await AuthenticatedAsync(fixture.AdminEmail, fixture.AdminPassword);

        var response = await caller.GetAsync(AdminListPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var users = await response.Content.ReadFromJsonAsync<UserResponse[]>();
        Assert.NotNull(users);
    }

    [Fact]
    public async Task StatusUpdate_WithoutToken_ReturnsUnauthorized()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Patch, $"/api/identity/users/{fixture.AdminId}/status")
        {
            Content = JsonContent.Create(new UpdateUserStatusRequest { IsActive = false })
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task StatusUpdate_AsOrdinaryUser_ReturnsForbidden_AndDoesNotChangeAnything()
    {
        var target = await CreateTargetAsync();
        using var developer = await AuthenticatedAsync(fixture.DeveloperEmail, fixture.DeveloperPassword);

        using var request = new HttpRequestMessage(
            HttpMethod.Patch, $"/api/identity/users/{target.Id}/status")
        {
            Content = JsonContent.Create(new UpdateUserStatusRequest { IsActive = false })
        };
        var response = await developer.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await ReadIsActiveAsync(target.Id));
    }

    // --- Listing semantics -----------------------------------------------------

    [Fact]
    public async Task AdminList_IncludesInactive_WhileNormalListStillHidesThem()
    {
        var target = await CreateTargetAsync();
        using var admin = await AuthenticatedAsync(fixture.AdminEmail, fixture.AdminPassword);

        // Deactivate through the real endpoint.
        using (var patch = new HttpRequestMessage(
            HttpMethod.Patch, $"/api/identity/users/{target.Id}/status")
        {
            Content = JsonContent.Create(new UpdateUserStatusRequest { IsActive = false })
        })
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(patch)).StatusCode);
        }

        var all = await admin.GetFromJsonAsync<UserResponse[]>(AdminListPath);
        Assert.NotNull(all);
        // The whole point of the admin route: an administrator can still SEE a
        // deactivated account, which the standard listing hides.
        Assert.Contains(all, item => item.Id == target.Id && !item.IsActive);

        var activeOnly = await admin.GetFromJsonAsync<UserResponse[]>("/api/identity/users");
        Assert.NotNull(activeOnly);
        // Backward compatibility: GET /users must still exclude inactive users.
        Assert.DoesNotContain(activeOnly, item => item.Id == target.Id);
        Assert.Contains(activeOnly, item => item.Id == fixture.AdminId);
    }

    [Fact]
    public async Task AdminList_NeverReturnsCredentialMaterial()
    {
        using var admin = await AuthenticatedAsync(fixture.AdminEmail, fixture.AdminPassword);
        var payload = await admin.GetStringAsync(AdminListPath);

        Assert.DoesNotContain("PasswordHash", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", payload, StringComparison.OrdinalIgnoreCase);
    }

    // --- Activation / deactivation --------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdminOrSecurityAdmin_CanDeactivateAndReactivate(bool securityAdmin)
    {
        var target = await CreateTargetAsync();
        using var caller = securityAdmin
            ? await AuthenticatedAsync(fixture.SecurityAdminEmail, fixture.SecurityAdminPassword)
            : await AuthenticatedAsync(fixture.AdminEmail, fixture.AdminPassword);

        var deactivated = await PatchStatusAsync(caller, target.Id, false);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        var afterOff = await deactivated.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(afterOff);
        Assert.False(afterOff.IsActive);
        // Everything else survives the status flip.
        Assert.Equal(target.Email, afterOff.Email);
        Assert.Equal(target.FullName, afterOff.FullName);

        var activated = await PatchStatusAsync(caller, target.Id, true);
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        var afterOn = await activated.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(afterOn);
        Assert.True(afterOn.IsActive);
    }

    [Fact]
    public async Task StatusUpdate_UnknownId_ReturnsNotFound()
    {
        using var admin = await AuthenticatedAsync(fixture.AdminEmail, fixture.AdminPassword);
        var response = await PatchStatusAsync(admin, Guid.NewGuid(), false);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StatusUpdate_RepeatedSameStatus_IsSafeAndReturnsOk()
    {
        var target = await CreateTargetAsync();
        using var admin = await AuthenticatedAsync(fixture.AdminEmail, fixture.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, (await PatchStatusAsync(admin, target.Id, false)).StatusCode);

        // Idempotent: a retried request must not fail the UI.
        var second = await PatchStatusAsync(admin, target.Id, false);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(body);
        Assert.False(body.IsActive);
    }

    [Fact]
    public async Task StatusUpdate_RequiresOnlyTheStatusField_NoFullUserPayload()
    {
        var target = await CreateTargetAsync();
        using var admin = await AuthenticatedAsync(fixture.AdminEmail, fixture.AdminPassword);

        // A body carrying IsActive alone must be accepted: the Admin UI never has to
        // resend FullName / Email / RoleId / DepartmentId to flip an account.
        var response = await PatchStatusAsync(admin, target.Id, false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(body);
        Assert.Equal(target.Email, body.Email);
    }

    // --- Login behaviour after a status change ---------------------------------

    [Fact]
    public async Task DeactivatedUser_CannotLogIn_AndCanLogInAgainOnceReactivated()
    {
        var target = await CreateTargetAsync($"login-target-{Guid.NewGuid():N}@example.com");
        using var admin = await AuthenticatedAsync(fixture.AdminEmail, fixture.AdminPassword);

        var beforeOff = await LoginAsync(target.Email);
        Assert.Equal(HttpStatusCode.OK, beforeOff.StatusCode);

        await PatchStatusAsync(admin, target.Id, false);

        // A future sign-in attempt for the deactivated account stays blocked.
        var whileOff = await LoginAsync(target.Email);
        Assert.Equal(HttpStatusCode.Unauthorized, whileOff.StatusCode);

        await PatchStatusAsync(admin, target.Id, true);

        var afterOn = await LoginAsync(target.Email);
        Assert.Equal(HttpStatusCode.OK, afterOn.StatusCode);
    }

    // --- Helpers ---------------------------------------------------------------

    private Task<HttpResponseMessage> LoginAsync(string email) =>
        client.PostAsJsonAsync("/api/identity/auth/login", new LoginRequest
        {
            Email = email,
            Password = fixture.TargetPassword
        });

    private async Task<HttpResponseMessage> PatchStatusAsync(HttpClient caller, Guid id, bool isActive)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Patch, $"/api/identity/users/{id}/status")
        {
            Content = JsonContent.Create(new UpdateUserStatusRequest { IsActive = isActive })
        };
        return await caller.SendAsync(request);
    }

    private async Task<UserResponse> CreateTargetAsync(string? email = null)
    {
        using var admin = await AuthenticatedAsync(fixture.AdminEmail, fixture.AdminPassword);
        var response = await admin.PostAsJsonAsync("/api/identity/users", new CreateUserRequest
        {
            FullName = "BIS-405 Target",
            Email = email ?? $"bis405-{Guid.NewGuid():N}@example.com",
            Password = fixture.TargetPassword,
            RoleId = fixture.RoleId,
            DepartmentId = fixture.DepartmentId
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(created);
        return created;
    }

    private async Task<bool> ReadIsActiveAsync(Guid id)
    {
        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return (await context.Users.IgnoreQueryFilters().SingleAsync(item => item.Id == id)).IsActive;
    }

    private async Task<HttpClient> AuthenticatedAsync(string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/identity/auth/login", new LoginRequest
        {
            Email = email, Password = password
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);

        var authenticated = fixture.CreateClient();
        authenticated.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.Token);
        return authenticated;
    }
}