using Analytics.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace Analytics.Service.Data;

/// <summary>
/// The Analytics Service's own database context. It deliberately targets a
/// dedicated analytics_db and never shares Audit Service's database: each
/// service consumes Kafka independently under its own consumer group, and
/// keeping separate stores preserves microservice database ownership.
/// </summary>
public class AnalyticsDbContext : DbContext
{
    public AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options)
        : base(options)
    {
    }

    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AnalyticsEvent>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Primary Kafka idempotency guard: the same event delivered twice
            // (redelivery, rebalance, rebalance after restart) can only ever
            // occupy one row.
            entity.HasIndex(e => e.EventId)
                .IsUnique();

            entity.Property(e => e.EventId)
                .IsRequired();

            entity.Property(e => e.SourceTopic)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.EventType)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.OccurredAt)
                .IsRequired();

            entity.Property(e => e.OccurredAtDate)
                .IsRequired();

            entity.Property(e => e.Actor)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(e => e.Resource)
                .HasMaxLength(250);

            entity.Property(e => e.ResourceName)
                .HasMaxLength(250);

            entity.Property(e => e.Action)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Outcome)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.DenialReason)
                .HasMaxLength(200);

            // Store the JSON payload efficiently in PostgreSQL, consistent with
            // the Audit Service. The raw metadata is preserved so a denial reason
            // or resource name can always be re-derived after a mapping change.
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .IsRequired();

            entity.Property(e => e.ConsumedAt)
                .IsRequired();

            // ------------------------------------------------------------------
            // Indexes supporting the three read endpoints.
            // ------------------------------------------------------------------

            // Date-range predicate shared by every analytics query.
            entity.HasIndex(e => e.OccurredAt)
                .HasDatabaseName("IX_AnalyticsEvents_OccurredAt");

            // Daily trend: GROUP BY day, filtered to the metric event types.
            entity.HasIndex(e => new { e.OccurredAtDate, e.EventType })
                .HasDatabaseName("IX_AnalyticsEvents_OccurredAtDate_EventType");

            // Metric counting within a window (COUNT(*) WHERE EventType = ...).
            entity.HasIndex(e => new { e.EventType, e.OccurredAt })
                .HasDatabaseName("IX_AnalyticsEvents_EventType_OccurredAt");

            // Reconciliation with the audit trail by originating topic.
            entity.HasIndex(e => new { e.SourceTopic, e.OccurredAt })
                .HasDatabaseName("IX_AnalyticsEvents_SourceTopic_OccurredAt");

            // Top requested resources group-by.
            entity.HasIndex(e => e.ResourceName)
                .HasDatabaseName("IX_AnalyticsEvents_ResourceName");

            // Denial-reason distribution group-by.
            entity.HasIndex(e => e.DenialReason)
                .HasDatabaseName("IX_AnalyticsEvents_DenialReason");
        });
    }
}
