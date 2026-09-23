using Audit.Service.Data;
using Microsoft.EntityFrameworkCore;
using Audit.Service.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Register DbContext
var connectionString = builder.Configuration.GetConnectionString("AuditDatabase")
    ?? throw new InvalidOperationException("ConnectionStrings:AuditDatabase or ConnectionStrings:DefaultConnection is required.");

// For PostgreSQL (requires Npgsql.EntityFrameworkCore.PostgreSQL package):
builder.Services.AddDbContext<AuditDbContext>(options =>
    options.UseNpgsql(connectionString));

// For SQL Server (requires Microsoft.EntityFrameworkCore.SqlServer package):
// builder.Services.AddDbContext<AuditDbContext>(options =>
//     options.UseSqlServer(connectionString));

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddScoped<IAuditService, AuditService>();

var app = builder.Build();

// 2. (Optional) Automatically apply migrations on startup in Development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    dbContext.Database.Migrate();

    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();