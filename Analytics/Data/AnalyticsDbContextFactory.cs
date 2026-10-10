using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Analytics.Service.Data;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` can construct the context
/// without booting the web host (and therefore without a JWT signing key or a
/// live Kafka broker). Mirrors Approval.Service.Data.ApprovalDbContextFactory.
/// </summary>
public sealed class AnalyticsDbContextFactory : IDesignTimeDbContextFactory<AnalyticsDbContext>
{
    public AnalyticsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__AnalyticsDatabase")
            ?? "Host=localhost;Database=analytics_db;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AnalyticsDbContext(options);
    }
}
