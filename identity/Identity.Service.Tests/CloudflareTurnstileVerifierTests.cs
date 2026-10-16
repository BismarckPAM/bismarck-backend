using Identity.Service.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Identity.Service.Tests;

public class CloudflareTurnstileVerifierTests
{
    [Theory]
    [InlineData("Production", "false", true)]
    [InlineData("Staging", "false", true)]
    [InlineData("Production", null, true)]
    [InlineData("Development", null, true)]
    [InlineData("Development", "invalid", true)]
    [InlineData("Development", "true", true)]
    [InlineData("Development", "false", false)]
    public async Task MissingTokenIsRejectedUnlessExplicitlyDisabledInDevelopment(
        string environmentName, string? enabled, bool expectedEnabled)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Turnstile:Enabled"] = enabled }).Build();
        using var client = new HttpClient();
        var verifier = new CloudflareTurnstileVerifier(client, configuration,
            NullLogger<CloudflareTurnstileVerifier>.Instance,
            new TestEnvironment { EnvironmentName = environmentName });

        Assert.Equal(expectedEnabled, verifier.IsEnabled);
        Assert.Equal(!expectedEnabled, await verifier.VerifyAsync(null, null));
        Assert.Equal(!expectedEnabled, await verifier.VerifyAsync("unverified-token", null));
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
