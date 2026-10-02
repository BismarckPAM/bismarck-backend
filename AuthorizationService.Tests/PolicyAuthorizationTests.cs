using System.Reflection;
using AuthorizationService.Controllers;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace AuthorizationService.Tests;

/// <summary>
/// BIS-405: server-side protection for access-policy administration.
///
/// Policy mutations decide what the platform grants, so they must not be
/// reachable by an arbitrary authenticated caller. These assertions lock the
/// authorization metadata in place so a future edit cannot silently reopen the
/// controller.
/// </summary>
public sealed class PolicyAuthorizationTests
{
    private const string AdminRoles = "Admin,Security Admin";

    private static IReadOnlyList<AuthorizeAttribute> AuthorizeDataFor(string methodName)
    {
        var action = typeof(AccessPoliciesController)
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(action);

        // An action-level attribute overrides the class-level one, so the effective
        // policy is the action's when present and the controller's otherwise.
        var effective = action.GetCustomAttributes<AuthorizeAttribute>().ToList();
        return effective.Count > 0 ? effective : ControllerAttributes();
    }

    private static IReadOnlyList<AuthorizeAttribute> ControllerAttributes()
        => typeof(AccessPoliciesController).GetCustomAttributes<AuthorizeAttribute>().ToList();

    [Fact]
    public void Controller_RequiresAuthentication()
    {
        // Without this the whole controller was anonymous: POST/PUT/DELETE were
        // reachable by anyone who could reach the port.
        var attributes = ControllerAttributes();
        Assert.Contains(attributes, attribute => attribute.Roles is null);

        // Nothing on the controller may opt out of authentication.
        Assert.DoesNotContain(
            typeof(AccessPoliciesController).GetCustomAttributes<AllowAnonymousAttribute>(),
            _ => true);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Update")]
    [InlineData("Delete")]
    public void Mutations_RequireAdminOrSecurityAdmin(string methodName)
    {
        var roles = AuthorizeDataFor(methodName)
            .Where(attribute => attribute.Roles is not null)
            .Select(attribute => attribute.Roles)
            .ToList();

        Assert.NotEmpty(roles);
        var effective = roles.First() ?? string.Empty;
        foreach (var role in AdminRoles.Split(','))
        {
            Assert.Contains(role, effective.Split(','));
        }
    }

    [Theory]
    [InlineData("GetAll")]
    [InlineData("GetById")]
    public void Reads_StayAuthenticatedButNotRoleRestricted(string methodName)
    {
        var roles = AuthorizeDataFor(methodName)
            .Where(attribute => attribute.Roles is not null)
            .ToList();

        // The Policy simulator and Access Check both read policies, so reads stay
        // open to any authenticated caller — but must never become anonymous.
        Assert.Empty(roles);
        Assert.NotEmpty(AuthorizeDataFor(methodName));
    }

    [Fact]
    public void PolicyEngine_IsUnaffected_ByControllerAuthorization()
    {
        // PolicyDecisionEngine reads the DbContext directly, never the HTTP
        // controller, so the decision path is untouched by these attributes.
        var engineFields = typeof(Services.PolicyDecisionEngine)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.DoesNotContain(
            engineFields,
            field => field.FieldType == typeof(AccessPoliciesController));
    }
}