using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Resource.Service.Data;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` can construct the context
/// without booting the full application (and without a live database).
/// </summary>
public sealed class ResourceDbContextFactory : IDesignTimeDbContextFactory<ResourceDbContext>
{
    public ResourceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ResourceDatabase")
            ?? "Host=localhost;Database=resource_db;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<ResourceDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ResourceDbContext(options);
    }
}