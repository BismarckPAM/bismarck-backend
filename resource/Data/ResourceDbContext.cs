using Microsoft.EntityFrameworkCore;
using Resource.Service.Models;
using ResourceModel = Resource.Service.Models.Resource;

namespace Resource.Service.Data;

public class ResourceDbContext(DbContextOptions<ResourceDbContext> options) : DbContext(options)
{
    public DbSet<ResourceModel> Resources => Set<ResourceModel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ResourceModel>(entity =>
        {
            entity.HasKey(resource => resource.Id);
            entity.Property(resource => resource.Type).HasMaxLength(100).IsRequired();
            entity.Property(resource => resource.Owner).HasMaxLength(200).IsRequired();
            entity.Property(resource => resource.Environment).HasMaxLength(100).IsRequired();
            entity.Property(resource => resource.Criticality)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.HasQueryFilter(resource => resource.IsActive);
        });
    }
}
