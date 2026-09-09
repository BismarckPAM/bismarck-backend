using Microsoft.EntityFrameworkCore;
using AuthorizationService.Models;

namespace AuthorizationService.Data;

public sealed class AuthorizationDbContext(DbContextOptions<AuthorizationDbContext> options)
    : DbContext(options)
{
    public DbSet<AccessPolicy> AccessPolicies => Set<AccessPolicy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccessPolicy>(entity =>
        {
            entity.HasKey(policy => policy.Id);
            entity.Property(policy => policy.Role).HasMaxLength(100).IsRequired();
            entity.Property(policy => policy.Environment).HasMaxLength(50).IsRequired();
            entity.Property(policy => policy.Criticality).HasMaxLength(20).IsRequired();
            entity.HasIndex(policy => new
            {
                policy.Role,
                policy.Environment,
                policy.Criticality
            });
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_AccessPolicies_MaxAccessLevel",
                "\"MaxAccessLevel\" BETWEEN 0 AND 5"));
        });
    }
}