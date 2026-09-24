using Microsoft.EntityFrameworkCore;
using Notification.Service.Models;

namespace Notification.Service.Data;

public class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Unique index to prevent duplicate notifications 
            entity.HasIndex(e => e.EventId)
                .IsUnique();

            // Optional composite index to optimize queries fetching a user's notification list
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });

            entity.Property(e => e.EventId)
                .IsRequired();

            entity.Property(e => e.UserId)
                .IsRequired()
                .HasMaxLength(150);

            entity.Property(e => e.EventType)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Title)
                .IsRequired()
                .HasMaxLength(250);

            entity.Property(e => e.Message)
                .IsRequired()
                .HasMaxLength(1000);

            entity.Property(e => e.ResourceId)
                .HasMaxLength(250);

            entity.Property(e => e.CreatedAt)
                .IsRequired();

            entity.Property(e => e.IsRead)
                .IsRequired()
                .HasDefaultValue(false);
        });
    }
}