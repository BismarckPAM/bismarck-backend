
using Notification.Service.Data;
using Microsoft.EntityFrameworkCore;
using Notification.Service.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Database
var connectionString = builder.Configuration.GetConnectionString("NotificationDatabase")
    ?? throw new InvalidOperationException("Connection string 'NotificationDatabase' not found.");

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(connectionString));

// JWT Authentication & Authorization
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var jwtKey = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY")
    ?? jwtSettings["SigningKey"];
if (string.IsNullOrWhiteSpace(jwtKey))
    throw new InvalidOperationException("JWT_SIGNING_KEY environment variable is required.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidAudience = jwtSettings["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddControllers();
builder.Services.AddHostedService<KafkaNotificationConsumer>();

var app = builder.Build();

// Development-only OpenAPI.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Internal HTTP traffic behind YARP.
if (!string.Equals(
        Environment.GetEnvironmentVariable("DISABLE_HTTPS_REDIRECTION"),
        "true",
        StringComparison.OrdinalIgnoreCase))
{
    app.UseHttpsRedirection();
}

// Prometheus monitoring BEFORE app.RunAsync().
app.UseHttpMetrics();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapMetrics("/metrics");

await app.RunAsync();
