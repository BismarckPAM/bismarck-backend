using Identity.Service.Data;
using Identity.Service.Models;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            var descriptor = services.Single(service => service.ServiceType == typeof(DbContextOptions<IdentityDbContext>));
            services.Remove(descriptor);
            services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(database.GetConnectionString()));
        });
    }

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await context.Database.EnsureCreatedAsync();
        var role = new Role { Id = Guid.NewGuid(), Name = "Admin" };
        var department = new Department { Id = Guid.NewGuid(), Name = "Engineering" };
        context.Roles.Add(role);
        context.Departments.Add(department);
        await context.SaveChangesAsync();
        RoleId = role.Id;
        DepartmentId = department.Id;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await database.DisposeAsync();
    }
}
