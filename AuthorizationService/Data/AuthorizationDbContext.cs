using Microsoft.EntityFrameworkCore;
using AuthorizationService.Models;

namespace AuthorizationService.Data;

public sealed class AuthorizationDbContext(DbContextOptions<AuthorizationDbContext> options)
    : DbContext(options)
{
    public DbSet<AccessPolicy> AccessPolicies => Set<AccessPolicy>();
    public DbSet<TemporaryPermission> TemporaryPermissions => Set<TemporaryPermission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccessPolicy>(entity =>
        {
            entity.HasKey(policy => policy.Id);
            entity.Property(policy => policy.Role).HasMaxLength(100).IsRequired();
            entity.Property(policy => policy.ResourceType).HasMaxLength(100).IsRequired();
            entity.Property(policy => policy.Environment).HasMaxLength(50).IsRequired();
            entity.Property(policy => policy.Criticality).HasMaxLength(20).IsRequired();
            entity.HasIndex(policy => new
            {
                policy.Role,
                policy.ResourceType,
                policy.Environment,
                policy.Criticality
            }).IsUnique();
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_AccessPolicies_MaxAccessLevel",
                "\"MaxAccessLevel\" BETWEEN 0 AND 5"));
        });

        modelBuilder.Entity<TemporaryPermission>(entity =>
        {
            entity.HasKey(permission => permission.Id);

            entity.HasIndex(permission => permission.ApprovalId)
                .IsUnique();

            entity.HasIndex(permission => new
            {
                permission.UserId,
                permission.ResourceId,
                permission.Status,
                permission.ExpiresAt
            });

            entity.Property(permission => permission.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            entity.ToTable(table => table.HasCheckConstraint(
                "CK_TemporaryPermissions_RequestedLevel",
                "\"RequestedLevel\" BETWEEN 1 AND 5"));
        });
    }
}