using AuthorizationService.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AuthorizationService.Tests;

/// <summary>
/// Covers the gate that decides whether a JIT session gets a real Azure RBAC
/// grant or degrades to a local-only session.
///
/// A subscription is always required to build an ARM scope. A credential is then
/// either the container's managed identity (preferred - no App Registration) or a
/// full client-credentials trio.
/// </summary>
public sealed class AzureJitOptionsTests
{
    private const string Subscription = "11111111-2222-3333-4444-555555555555";
    private const string Tenant = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string Client = "99999999-8888-7777-6666-555555555555";
    private const string Secret = "not-a-real-secret";

    [Fact]
    public void IsConfigured_IsFalse_WhenSubscriptionIsMissing()
    {
        var options = new AzureJitOptions { UseManagedIdentity = true };

        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void IsConfigured_IsTrue_WithSubscriptionAndManagedIdentity()
    {
        // No tenant/client/secret at all - this is the Container Apps path.
        var options = new AzureJitOptions
        {
            SubscriptionId = Subscription,
            UseManagedIdentity = true
        };

        Assert.True(options.IsConfigured);
        Assert.False(options.HasClientSecret);
    }

    [Fact]
    public void IsConfigured_IsTrue_WithSubscriptionAndClientSecret()
    {
        // Legacy local-dev path, with managed identity explicitly disabled.
        var options = new AzureJitOptions
        {
            SubscriptionId = Subscription,
            TenantId = Tenant,
            ClientId = Client,
            ClientSecret = Secret,
            UseManagedIdentity = false
        };

        Assert.True(options.IsConfigured);
        Assert.True(options.HasClientSecret);
    }

    [Fact]
    public void IsConfigured_IsFalse_WhenManagedIdentityDisabledAndNoClientSecret()
    {
        var options = new AzureJitOptions
        {
            SubscriptionId = Subscription,
            UseManagedIdentity = false
        };

        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void HasClientSecret_RequiresAllThreeValues()
    {
        var partial = new AzureJitOptions
        {
            TenantId = Tenant,
            ClientId = Client
            // ClientSecret deliberately missing
        };

        Assert.False(partial.HasClientSecret);
    }

    [Theory]
    // NOTE: the configuration binder uses bool.Parse, so only "true"/"false"
    // (any casing) are accepted - "1"/"0" would throw at startup. The default when
    // the key is absent is true, because managed identity is the preferred path.
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("FALSE", false)]
    public void UseManagedIdentity_IsReadFromConfiguration_DefaultingToTrue(
        string? configured,
        bool expected)
    {
        // The switch is a config value (AzureJit:UseManagedIdentity), not an
        // environment variable, so this is deterministic regardless of the
        // ambient AZURE_* environment.
        var values = configured is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { ["AzureJit:UseManagedIdentity"] = configured };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var options = AzureJitOptions.FromConfiguration(configuration);

        Assert.Equal(expected, options.UseManagedIdentity);
    }
}