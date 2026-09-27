using Identity.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace Identity.Service.Data;

public class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<OnboardingTicket> OnboardingTickets => Set<OnboardingTicket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.Property(user => user.FullName).HasMaxLength(200).IsRequired();
            entity.Property(user => user.Email).HasMaxLength(320).IsRequired();
            entity.Property(user => user.PasswordHash).HasMaxLength(500).IsRequired();
            entity.HasIndex(user => user.Email).IsUnique();
            entity.HasQueryFilter(user => user.IsActive);

            entity.HasOne(user => user.Role)
                .WithMany(role => role.Users)
                .HasForeignKey(user => user.RoleId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(user => user.Department)
                .WithMany(department => department.Users)
                .HasForeignKey(user => user.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(role => role.Id);
            entity.Property(role => role.Name).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(department => department.Id);
            entity.Property(department => department.Name).HasMaxLength(150).IsRequired();
        });

        modelBuilder.Entity<OnboardingTicket>(entity =>
        {
            entity.HasKey(ticket => ticket.Id);
            entity.Property(ticket => ticket.FullName).HasMaxLength(200).IsRequired();
            entity.Property(ticket => ticket.Email).HasMaxLength(320).IsRequired();
            entity.Property(ticket => ticket.Department).HasMaxLength(150).IsRequired();
            entity.Property(ticket => ticket.RequestedRole).HasMaxLength(100).IsRequired();
            entity.Property(ticket => ticket.Justification).HasMaxLength(2000).IsRequired();
            entity.Property(ticket => ticket.Status).HasMaxLength(20).IsRequired();
            entity.Property(ticket => ticket.ReviewedBy).HasMaxLength(320);
            entity.Property(ticket => ticket.RejectionReason).HasMaxLength(1000);
            entity.HasIndex(ticket => ticket.Email);
            entity.HasIndex(ticket => ticket.Status);
        });
    }
}
