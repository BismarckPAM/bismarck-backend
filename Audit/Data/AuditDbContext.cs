using Microsoft.EntityFrameworkCore;
using Audit.Service.Models;

namespace Audit.Service.Data;

public class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Ensure idempotency: deduplicate duplicate Kafka messages
            entity.HasIndex(e => e.EventId)
                .IsUnique();

            entity.Property(e => e.EventId)
                .IsRequired();

            entity.Property(e => e.EventType)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.OccurredAt)
                .IsRequired();

            entity.Property(e => e.Actor)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(e => e.Resource)
                .HasMaxLength(250);

            entity.Property(e => e.Action)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Outcome)
                .IsRequired()
                .HasMaxLength(50);

            // Store JSON payload efficiently in PostgreSQL
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .IsRequired();

            entity.Property(e => e.ConsumedAt)
                .IsRequired();
        });
    }
}