using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Approval.Service.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace Approval.Service.IntegrationTests;

public sealed class ApprovalApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestSigningKey = "SuperSecretTestKeyThatIsAtLeast32BytesLongForHmacSha256!";
    public const string TestIssuer = "Identity.Service";
    public const string TestAudience = "Bismarck.Services";

    private readonly PostgreSqlContainer database = new PostgreSqlBuilder()
        .WithDatabase("approvaldb")
        .WithUsername("admin")
        .WithPassword("admin_password")
        .Build();

    public ApprovalApiFixture()
    {
        Environment.SetEnvironmentVariable("JWT_SIGNING_KEY", TestSigningKey);
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__ApprovalDatabase",
            "Host=localhost;Database=approvaldb;Username=admin;Password=admin_password");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("JwtSettings:SigningKey", TestSigningKey);
        builder.ConfigureServices(services =>
        {
            var descriptor = services.Single(
                service => service.ServiceType == typeof(DbContextOptions<ApprovalDbContext>));
            services.Remove(descriptor);
            services.AddDbContext<ApprovalDbContext>(options =>
                options.UseNpgsql(database.GetConnectionString()));
        });
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApprovalDbContext>();
        await context.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await database.DisposeAsync();
    }

    public static string GenerateValidJwtToken(
        string userId = "requester-1",
        string role = "Admin")
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(TestSigningKey);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId),
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
