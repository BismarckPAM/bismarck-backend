using System.Text;
using Audit.Service.Data;
using Audit.Service.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

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
            ClockSkew = TimeSpan.Zero // Optional: removes the default 5-minute clock drift grace period
        };
    });

builder.Services.AddAuthorization();

// Application Services & Controllers
builder.Services.AddControllers();
builder.Services.AddScoped<IAuditService, AuditService>();

// The Kafka consumer is registered only when enabled, matching the Analytics
// Service (BIS-402). Integration tests switch it off so the real host can start
// without a broker - the Audit rows they query are seeded straight into the
// database, which is exactly what this consumer would otherwise write.
// Normal deployments keep the default (enabled), so production behaviour is
// unchanged.
if (builder.Configuration.GetValue("Kafka:Enabled", true))
{
    builder.Services.AddHostedService<KafkaAuditConsumer>();
}

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Behind the API Gateway TLS is already terminated, and internal traffic is
// plain HTTP. HttpsRedirection here would emit a 307 back to the client for
// every proxied request, so it must be switchable off.
if (!string.Equals(
        Environment.GetEnvironmentVariable("DISABLE_HTTPS_REDIRECTION"),
        "true",
        StringComparison.OrdinalIgnoreCase))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

/// <summary>
/// Exposed so the integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>,
/// matching the pattern already used by the Analytics (BIS-402), Approval and
/// Resource test suites.
/// </summary>
public partial class Program { }