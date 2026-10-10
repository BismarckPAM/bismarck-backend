using System.Data.Common;
using Audit.Service.Data;
using Audit.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace Audit.Service.Tests;

/// <summary>
/// Test-only context used by the unit suite.
/// <para>
/// Two SQLite incompatibilities with the production model are adapted here, and
/// only here - the service itself is never modified:
/// </para>
/// <list type="number">
///   <item>SQLite cannot order or compare <see cref="DateTimeOffset"/> columns, so
///         <c>OccurredAt</c> is stored as UTC ticks. That keeps the date filters and
///         the date ordering genuinely numeric and genuinely SQL. On PostgreSQL
///         (integration tests) <c>OccurredAt</c> stays a real <c>timestamptz</c>.</item>
///   <item><c>Metadata</c> is declared as <c>jsonb</c>, a type SQLite does not
///         know; it is mapped to <c>TEXT</c> instead. The column is opaque to every
///         query the viewer issues.</item>
/// </list>
/// </summary>
public sealed class SqliteAuditDbContext : AuditDbContext
{
    public SqliteAuditDbContext(DbConnection connection)
        : base(new DbContextOptionsBuilder<AuditDbContext>()
            .UseSqlite(connection)
            .Options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditLog>()
            .Property(e => e.OccurredAt)
            .HasConversion(
                value => value.UtcTicks,
                ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

        modelBuilder.Entity<AuditLog>()
            .Property(e => e.Metadata)
            .HasColumnType("TEXT");
    }
}