using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Approval.Service.Data;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` can construct the context
/// without booting the full application (and without a live database).
/// </summary>
public sealed class ApprovalDbContextFactory : IDesignTimeDbContextFactory<ApprovalDbContext>
{
    public ApprovalDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ApprovalDatabase")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Database=bismarck_approval;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<ApprovalDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ApprovalDbContext(options);
    }
}