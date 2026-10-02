using System.Data.Common;
using Analytics.Service.Data;
using Analytics.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace Analytics.Service.Tests;

/// <summary>
/// Test-only context used by the unit suite.
/// <para>
/// The SQLite provider cannot order or compare <see cref="DateTimeOffset"/>
/// columns, which would make every date-range test impossible to run in-process.
/// Storing the value as UTC ticks keeps the comparison genuinely numeric and the
/// query genuinely SQL, without changing the production model: on PostgreSQL
/// (integration tests) <c>OccurredAt</c> is a real <c>timestamptz</c>.
/// </para>
/// <para>
/// This type exists only in the test project; the service itself is unmodified.
/// </para>
/// </summary>
public sealed class SqliteAnalyticsDbContext : AnalyticsDbContext
{
    public SqliteAnalyticsDbContext(DbConnection connection)
        : base(new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseSqlite(connection)
            .Options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AnalyticsEvent>()
            .Property(e => e.OccurredAt)
            .HasConversion(
                value => value.UtcTicks,
                ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
    }
}
