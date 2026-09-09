using AuthorizationService.Data;
using AuthorizationService.Middleware;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = builder.Configuration.GetConnectionString("AuthorizationDatabase")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:AuthorizationDatabase is required.");

builder.Services.AddDbContext<AuthorizationDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<AuthorizationDbContext>("authorization-database");

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.IsStaging() || app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseMiddleware<ExceptionHandlerMiddleware>();
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

public partial class Program { }
