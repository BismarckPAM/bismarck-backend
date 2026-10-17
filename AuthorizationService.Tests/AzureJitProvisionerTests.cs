using Azure.Core;
using AuthorizationService.Models;
using AuthorizationService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using System.Net;
using System.Net.Http.Json;

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

    [Fact]
    public async Task GrantAsync_CreatesRoleAssignmentAtVmScope()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var provisioner = CreateProvisioner(new Dictionary<string, string?>
        {
            ["AzureJit:SubscriptionId"] = "subscription",
            ["AzureJit:PrincipalId"] = "principal"
        }, handler);
        var permission = new TemporaryPermission
        {
            Id = Guid.NewGuid(), RequestedLevel = 4, TargetVmName = "vm",
            TargetResourceGroup = "rg"
        };

        var result = await provisioner.GrantAsync(permission);

        Assert.True(result.Succeeded);
        Assert.Contains("/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm", result.Detail);
        Assert.Equal(HttpMethod.Put, handler.Method);
        Assert.Contains("Authorization", handler.Headers.Keys);
        Assert.NotNull(permission.AzureScope);
    }

    [Fact]
    public async Task GrantAsync_ReturnsFailureForArmErrorAndRevokeAcceptsNotFound()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest)
        {
            Content = "{\"error\":\"bad request\"}"
        };
        var provisioner = CreateProvisioner(new Dictionary<string, string?>
        {
            ["AzureJit:SubscriptionId"] = "subscription",
            ["AzureJit:PrincipalId"] = "principal"
        }, handler);
        var grant = await provisioner.GrantAsync(new TemporaryPermission());
        Assert.False(grant.Succeeded);

        handler.StatusCode = HttpStatusCode.NotFound;
        var revoke = await provisioner.RevokeAsync(new TemporaryPermission
        {
            CloudRoleAssignmentId = "/subscriptions/subscription/roleAssignments/id"
        });
        Assert.True(revoke.Succeeded);
        Assert.Equal(HttpMethod.Delete, handler.Method);
    }

    private static AzureJitProvisioner CreateProvisioner(
        IDictionary<string, string?> values,
        HttpMessageHandler? handler = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var credential = new TestCredential();
        return new AzureJitProvisioner(
            new HttpClient(handler ?? new RecordingHandler(HttpStatusCode.OK)),
            configuration,
            credential,
            NullLogger<AzureJitProvisioner>.Instance);
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = statusCode;
        public string Content { get; set; } = "{}";
        public HttpMethod? Method { get; private set; }
        public Dictionary<string, IEnumerable<string>> Headers { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            foreach (var header in request.Headers)
                Headers[header.Key] = header.Value;
            return Task.FromResult(new HttpResponseMessage(StatusCode)
            {
                Content = JsonContent.Create(Content)
            });
        }

    }

    private sealed class TestCredential : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AccessToken("test-token", DateTimeOffset.UtcNow.AddHours(1)));
    }
}
