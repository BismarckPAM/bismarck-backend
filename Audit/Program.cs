
using System.Text;
using Audit.Service.Data;
using Audit.Service.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

// Database
var connectionString = builder.Configuration.GetConnectionString("AuditDatabase")
    ?? throw new InvalidOperationException("Connection string 'AuditDatabase' not found.");

builder.Services.AddDbContext<AuditDbContext>(options =>
    options.UseNpgsql(connectionString));

// JWT Authentication & Authorization
var jwtKey = builder.Configuration["Jwt:Key"] 
    ?? builder.Configuration["Jwt:Secret"] 
    ?? throw new InvalidOperationException("JWT Secret Key is not configured.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// Application Services & Controllers
builder.Services.AddControllers();
builder.Services.AddScoped<IAuditService, AuditService>();

// Existing Kafka consumer registration is unchanged.
if (builder.Configuration.GetValue("Kafka:Enabled", true))
{
    builder.Services.AddHostedService<KafkaAuditConsumer>();
}

builder.Services.AddOpenApi();

var app = builder.Build();


if (app.Environment.IsDevelopment()
    || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.MapOpenApi();
}


// HTTPS redirection must remain configurable behind YARP.
if (!string.Equals(
        Environment.GetEnvironmentVariable("DISABLE_HTTPS_REDIRECTION"),
        "true",
        StringComparison.OrdinalIgnoreCase))
{
    app.UseHttpsRedirection();
}

// Prometheus HTTP metrics.
app.UseHttpMetrics();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapMetrics("/metrics");

await app.RunAsync();

/// <summary>
/// Exposed to integration tests using WebApplicationFactory.
/// </summary>
public partial class Program { }
