using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Resource.Service.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace Resource.Service.IntegrationTests.Fixtures;

public class ResourceApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestSigningKey = "SuperSecretTestKeyThatIsAtLeast32BytesLongForHmacSha256!";
    public const string TestIssuer = "BismarckIdentityService";
    public const string TestAudience = "Bismarck.Services";

    static ResourceApiFactory()
    {
        Environment.SetEnvironmentVariable("JWT_SIGNING_KEY", TestSigningKey);
        Environment.SetEnvironmentVariable("RESOURCE_DB_PASSWORD", "test_db_password");
    }

    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("resource_db")
        .WithUsername("admin")
        .WithPassword("admin_password")
        .Build();

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ResourceDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await _dbContainer.StopAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ResourceDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<ResourceDbContext>(options =>
                options.UseNpgsql(_dbContainer.GetConnectionString()));
        });
    }

    public ResourceDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ResourceDbContext>();
    }

    public static string GenerateValidJwtToken(string userId = "test-user-id", string email = "admin@bismarck.local")
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(TestSigningKey);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Email, email),
                new Claim("sub", userId)
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = TestIssuer,
            Audience = TestAudience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}
