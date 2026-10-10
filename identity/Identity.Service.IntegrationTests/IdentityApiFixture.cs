using Identity.Service.Data;
using Identity.Service.Models;
using Identity.Service.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Identity.Service.IntegrationTests;

public sealed class IdentityApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder()
        .WithDatabase("identity_db")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public Guid RoleId { get; private set; }
    public Guid DepartmentId { get; private set; }
    public Guid AdminId { get; private set; }
    public string AdminEmail { get; } = "admin@example.com";
    public string AdminPassword { get; } = "AdminPassword123!";

    // --- BIS-405: a Security Admin is a distinct role from Admin. It is seeded in
    // the TEST database only (never via a production migration) so the Admin
    // Dashboard's two allowed roles can both be exercised end-to-end.
    public Guid SecurityAdminRoleId { get; private set; }
    public Guid SecurityAdminId { get; private set; }
    public string SecurityAdminEmail { get; } = "secadmin@example.com";
    public string SecurityAdminPassword { get; } = "SecurityAdminPassword123!";

    // --- BIS-405: a non-privileged account used for the 403 assertions.
    public Guid DeveloperId { get; private set; }
    public string DeveloperEmail { get; } = "developer@example.com";
    public string DeveloperPassword { get; } = "DeveloperPassword123!";

    public string TargetPassword { get; } = "TargetPassword123!";

    public IdentityApiFixture()
    {
        Environment.SetEnvironmentVariable("IDENTITY_DB_PASSWORD", "postgres");
        Environment.SetEnvironmentVariable("JWT_SIGNING_KEY", "integration-test-signing-key-that-is-at-least-32-characters");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("JwtSettings:SigningKey", "integration-test-signing-key-that-is-at-least-32-characters");
        builder.ConfigureServices(services =>
        {
            var descriptor = services.Single(service => service.ServiceType == typeof(DbContextOptions<IdentityDbContext>));
            services.Remove(descriptor);
            services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(database.GetConnectionString()));

            // Replace the production Kafka publisher with a no-op. Integration
            // tests run without a Kafka broker; the real KafkaDomainEventPublisher
            // blocks on an unreachable broker and previously hung the test run
            // (TaskCanceledException after the HTTP request timed out).
            var publisherDescriptor = services.SingleOrDefault(
                service => service.ServiceType == typeof(IDomainEventPublisher));
            if (publisherDescriptor is not null)
            {
                services.Remove(publisherDescriptor);
            }

            services.AddSingleton<IDomainEventPublisher, NullDomainEventPublisher>();
        });
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await context.Database.EnsureCreatedAsync();
        var role = new Role { Id = Guid.NewGuid(), Name = "Admin" };
        var securityAdminRole = new Role { Id = Guid.NewGuid(), Name = "Security Admin" };
        var developerRole = new Role { Id = Guid.NewGuid(), Name = "Developer" };
        var department = new Department { Id = Guid.NewGuid(), Name = "Engineering" };
        context.Roles.AddRange(role, securityAdminRole, developerRole);
        context.Departments.Add(department);
        var admin = new User
        {
            Id = Guid.NewGuid(), FullName = "Integration Admin", Email = AdminEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(AdminPassword),
            RoleId = role.Id, DepartmentId = department.Id
        };
        var securityAdmin = new User
        {
            Id = Guid.NewGuid(), FullName = "Integration Security Admin", Email = SecurityAdminEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(SecurityAdminPassword),
            RoleId = securityAdminRole.Id, DepartmentId = department.Id
        };
        var developer = new User
        {
            Id = Guid.NewGuid(), FullName = "Integration Developer", Email = DeveloperEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DeveloperPassword),
            RoleId = developerRole.Id, DepartmentId = department.Id
        };
        context.Users.AddRange(admin, securityAdmin, developer);
        await context.SaveChangesAsync();
        RoleId = role.Id;
        DepartmentId = department.Id;
        AdminId = admin.Id;
        SecurityAdminRoleId = securityAdminRole.Id;
        SecurityAdminId = securityAdmin.Id;
        DeveloperId = developer.Id;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await database.DisposeAsync();
    }
}
