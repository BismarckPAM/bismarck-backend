
using System.Text;
using Analytics.Service.Data;
using Analytics.Service.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Analytics owns its own database.
var connectionString = builder.Configuration.GetConnectionString("AnalyticsDatabase")
    ?? throw new InvalidOperationException(
        "Connection string 'AnalyticsDatabase' not found. Set ConnectionStrings__AnalyticsDatabase.");

builder.Services.AddDbContext<AnalyticsDbContext>(options =>
    options.UseNpgsql(connectionString));

// JWT authentication.
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var signingKey = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY")
    ?? jwtSettings["SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey))
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Bismarck PAM - Analytics API",
        Version = "v1",
        Description =
            "Aggregated access, approval, denial and revocation metrics (BIS-402). " +
            "Metrics are derived from ApprovalRequested, ApprovalGranted, " +
            "AccessDenied, ApprovalRejected and PermissionRevoked events."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter JWT Bearer token issued by the Identity Service."
    });

    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
        }
    });
});

builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();

// Existing Kafka consumer remains unchanged.
if (builder.Configuration.GetValue("Kafka:Enabled", true))
{
    builder.Services.AddHostedService<KafkaAnalyticsConsumer>();
}

var app = builder.Build();


if (app.Environment.IsDevelopment()
    || app.Environment.IsStaging()
    || app.Environment.IsEnvironment("Testing")
    || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


// HTTP ingress behind the gateway.
if (!string.Equals(
        Environment.GetEnvironmentVariable("DISABLE_HTTPS_REDIRECTION"),
        "true",
        StringComparison.OrdinalIgnoreCase))
{
    app.UseHttpsRedirection();
}

// Prometheus monitoring.
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
