using Azure.Core;
using AuthorizationService.Models;
using AuthorizationService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AuthorizationService.Tests;

public sealed class AzureJitProvisionerTests
{
    [Fact]
    public async Task GrantAsync_WhenAzureIsNotConfigured_ReturnsLocalOnlyResult()
    {
        var provisioner = CreateProvisioner(new Dictionary<string, string?>());

        var result = await provisioner.GrantAsync(new TemporaryPermission());

        Assert.False(result.Succeeded);
        Assert.Contains("not configured", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RevokeAsync_WhenNoAssignmentExists_ReturnsFailureWithoutAzureCall()
    {
        var provisioner = CreateProvisioner(new Dictionary<string, string?>
        {
            ["AzureJit:SubscriptionId"] = "subscription"
        });

        var result = await provisioner.RevokeAsync(new TemporaryPermission());

        Assert.False(result.Succeeded);
        Assert.Contains("No cloud role assignment", result.Detail);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void AzureJitOptions_IsConfiguredRequiresSubscriptionAndCredential(
        bool useManagedIdentity,
        bool expected)
    {
        var options = new AzureJitOptions
        {
            SubscriptionId = "subscription",
            UseManagedIdentity = useManagedIdentity,
            TenantId = useManagedIdentity ? null : "tenant",
            ClientId = useManagedIdentity ? null : "client",
            ClientSecret = useManagedIdentity ? null : "secret"
        };

        Assert.Equal(expected, options.IsConfigured);
        Assert.Equal(!useManagedIdentity, options.HasClientSecret);
    }

    private static AzureJitProvisioner CreateProvisioner(
        IDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var credential = new Mock<TokenCredential>().Object;
        return new AzureJitProvisioner(
            new HttpClient(),
            configuration,
            credential,
            NullLogger<AzureJitProvisioner>.Instance);
    }
}
