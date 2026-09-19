using Microsoft.EntityFrameworkCore;
using Approval.Service.Models;

namespace Approval.Service.Data;

public class ApprovalDbContext(DbContextOptions<ApprovalDbContext> options) : DbContext(options)
{
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApprovalRequest>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.RequesterUserId)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.ResourceId)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Reason)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.DurationMinutes)
                .IsRequired();

            // Store Enum as string in PostgreSQL database for readability
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(e => e.RejectionReason)
                .HasMaxLength(500);

            entity.Property(e => e.ReviewedByUserId)
                .HasMaxLength(100);
        });
    }
}