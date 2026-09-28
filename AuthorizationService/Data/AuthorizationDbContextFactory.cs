using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AuthorizationService.Data;

/// <summary>
/// Design-time factory so `dotnet ef migrations` can build the context without
/// booting the full host (no Kafka, identity or resource dependencies).
/// </summary>
public sealed class AuthorizationDbContextFactory : IDesignTimeDbContextFactory<AuthorizationDbContext>
{
    public AuthorizationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__AuthorizationDatabase")
            ?? "Host=localhost;Database=authorization_db;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AuthorizationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AuthorizationDbContext(options);
    }
}