using System.Text;
using Analytics.Service.Data;
using Analytics.Service.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// ---------------------------------------------------------------------------
// Database - the Analytics Service owns analytics_db exclusively.
// It never reads or writes the Audit Service's database; both services consume
// Kafka independently under their own consumer group.
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("AnalyticsDatabase")
    ?? throw new InvalidOperationException(
        "Connection string 'AnalyticsDatabase' not found. Set ConnectionStrings__AnalyticsDatabase.");

builder.Services.AddDbContext<AnalyticsDbContext>(options =>
    options.UseNpgsql(connectionString));

// ---------------------------------------------------------------------------
// JWT - identical contract to Identity Service (and to the other secure
// services): HS256, issuer Identity.Service, audience Bismarck.Services.
// The signing key comes from JWT_SIGNING_KEY, falling back to configuration.
// ---------------------------------------------------------------------------
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

// Swagger/OpenAPI with Bearer auth support (DoD-6). Matches the Swashbuckle
// version already used by the Resource Service. No secrets are exposed.
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

// The Kafka consumer is registered only when enabled. Integration tests disable
// it so the real host can start without a broker; normal deployments leave the
// default (enabled) in place.
if (builder.Configuration.GetValue("Kafka:Enabled", true))
{
    builder.Services.AddHostedService<KafkaAnalyticsConsumer>();
}

var app = builder.Build();

if (app.Environment.IsDevelopment()
    || app.Environment.IsStaging()
    || app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
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
/// matching the pattern already used by the Approval and Resource test suites.
/// </summary>
public partial class Program { }
