using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Analytics.Service.Data;
using Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace Analytics.Service.IntegrationTests;

/// <summary>
/// Boots the real Analytics host against a throwaway PostgreSQL container with
/// the real InitialCreate migration applied, so the integration suite exercises
/// genuine PostgreSQL SQL (jsonb columns, timestamptz comparisons, group-by
/// aggregates) rather than a substitute provider.
/// <para>
/// No Kafka broker is started: the hosted consumer is switched off through the
/// <c>Kafka:Enabled</c> setting, because the events it would receive are exactly
/// what the tests seed directly through the DbContext.
/// </para>
/// </summary>
public sealed class AnalyticsApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Test-only signing key. Never used outside the test host.
    public const string TestSigningKey = "SuperSecretTestKeyThatIsAtLeast32BytesLongForHmacSha256!";
    public const string TestIssuer = "Identity.Service";
    public const string TestAudience = "Bismarck.Services";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("analytics_db")
        .WithUsername("admin")
        .WithPassword("admin_password")
        .Build();

    // The mapped port is only known once the container has started, so the
    // connection string is resolved lazily rather than in the constructor.
    private string ConnectionString => _database.GetConnectionString();

    public AnalyticsApiFixture()
    {
        // The host reads its signing key from the environment, exactly as in
        // production, so the JWT contract is not bypassed.
        Environment.SetEnvironmentVariable("JWT_SIGNING_KEY", TestSigningKey);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                service => service.ServiceType == typeof(DbContextOptions<AnalyticsDbContext>));

            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AnalyticsDbContext>(options =>
                options.UseNpgsql(ConnectionString));

            // Disable the Kafka consumer so the host starts without a broker.
            services.Configure<Microsoft.Extensions.Configuration.IConfiguration>(configuration =>
                configuration["Kafka:Enabled"] = "false");
        });
    }

    public async Task InitializeAsync()
    {
        await _database.StartAsync();

        // The service is configured in ConfigureWebHost, so it must only be
        // touched after the container exists.
        Environment.SetEnvironmentVariable("ConnectionStrings__AnalyticsDatabase", ConnectionString);

        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
        await context.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    /// <summary>Creates a client with a valid Identity Service JWT attached.</summary>
    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateValidJwtToken());
        return client;
    }

    /// <summary>Runs an action against a fresh scope, e.g. to seed events.</summary>
    public async Task WithDbContextAsync(Func<AnalyticsDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
        await action(context);
    }

    /// <summary>Removes every stored event so each test starts from a known state.</summary>
    public Task ResetAsync() =>
        WithDbContextAsync(context => context.AnalyticsEvents.ExecuteDeleteAsync());

    public static string GenerateValidJwtToken(string userId = "security-admin", string role = "Admin")
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(TestSigningKey);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, userId),
                new Claim(ClaimTypes.Role, role),
                new Claim(JwtRegisteredClaimNames.Sub, userId)
            ]),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = TestIssuer,
            Audience = TestAudience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        return tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));
    }
}
