using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Resource.Service.Data;
using Resource.Service.Filters;
using Resource.Service.HealthChecks;
using Resource.Service.Mappings;
using Resource.Service.Middleware;
using Resource.Service.Services;
using Resource.Service.Validators;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options =>
    {
        options.Filters.Add<ModelStateValidationFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true;
});

var signingKey = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY")
    ?? throw new InvalidOperationException("JWT_SIGNING_KEY environment variable is required.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
            ValidAudience = builder.Configuration["JwtSettings:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

var allowedOrigin = builder.Configuration["FRONTEND_ORIGIN"] ?? "http://localhost:5173";

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigin)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter JWT Bearer token"
    });

    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
        }
    });
});

var dbPassword = Environment.GetEnvironmentVariable("RESOURCE_DB_PASSWORD")
    ?? throw new InvalidOperationException("RESOURCE_DB_PASSWORD environment variable is required.");

var resourceConnectionString = builder.Configuration.GetConnectionString("ResourceDatabase")
    + $";Password={dbPassword}";

builder.Services.AddDbContext<ResourceDbContext>(options =>
    options.UseNpgsql(resourceConnectionString));
    
builder.Services.AddAutoMapper(config => config.AddProfile<MappingProfile>());
builder.Services.AddValidatorsFromAssemblyContaining<CreateResourceRequestValidator>();
builder.Services.AddScoped<IResourceService, ResourceService>();

var kafkaBootstrap = builder.Configuration["Kafka:BootstrapServers"] ?? "kafka:9092";
builder.Services.AddSingleton(new KafkaHealthCheck(kafkaBootstrap));
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ResourceDbContext>("resource-database")
    .AddCheck<KafkaHealthCheck>("kafka");

var app = builder.Build();

app.UseMiddleware<ExceptionHandlerMiddleware>();

if (app.Environment.IsDevelopment() || app.Environment.IsStaging() || app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!string.Equals(
        Environment.GetEnvironmentVariable("DISABLE_HTTPS_REDIRECTION"),
        "true",
        StringComparison.OrdinalIgnoreCase))
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteResponse
});

app.Run();

public partial class Program { }

/// <summary>
/// Writes a structured JSON health report (overall status plus a per-check
/// breakdown) instead of the framework's default plain-text response, so
/// /health genuinely reports individual dependency status rather than just
/// an aggregate string.
/// </summary>
internal static class HealthCheckResponseWriter
{
    public static Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = entry.Value.Duration.TotalMilliseconds
            }),
            totalDurationMs = report.TotalDuration.TotalMilliseconds
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}