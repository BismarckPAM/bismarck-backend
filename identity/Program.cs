using FluentValidation;
using Identity.Service.Data;
using Identity.Service.HealthChecks;
using Identity.Service.Mappings;
using Identity.Service.Middleware;
using Identity.Service.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste a JWT bearer token here."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
    });
});
var dbPassword = Environment.GetEnvironmentVariable("IDENTITY_DB_PASSWORD")
    ?? throw new InvalidOperationException("IDENTITY_DB_PASSWORD environment variable is required.");
var connectionString = builder.Configuration.GetConnectionString("IdentityDatabase") + $";Password={dbPassword}";

builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddAutoMapper(config => config.AddProfile<MappingProfile>());
builder.Services.AddValidatorsFromAssemblyContaining<MappingProfile>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<ITokenService, TokenService>();

var kafkaBootstrap = builder.Configuration["Kafka:BootstrapServers"] ?? "kafka:9092";
builder.Services.AddSingleton(new KafkaHealthCheck(kafkaBootstrap));
builder.Services.AddHealthChecks()
    .AddDbContextCheck<IdentityDbContext>("identity-database")
    .AddCheck<KafkaHealthCheck>("kafka");

var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var signingKey = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY") ?? jwtSettings["SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey))
    throw new InvalidOperationException("JWT_SIGNING_KEY environment variable is required.");
builder.Configuration["JwtSettings:SigningKey"] = signingKey;
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
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
app.UseMiddleware<ExceptionHandlerMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthCheckResponseWriter.WriteResponse
});

app.MapPost("/api/test/kafka/publish", async (string message) =>
{
    var config = new ProducerConfig { BootstrapServers = kafkaBootstrap };
    using var producer = new ProducerBuilder<Null, string>(config).Build();
    var result = await producer.ProduceAsync("pam.test.events", new Message<Null, string>
    {
        Value = message
    });
    return Results.Ok(new
    {
        status = "Published",
        topic = result.Topic,
        partition = result.Partition.Value,
        offset = result.Offset.Value,
        payload = message
    });
});

app.MapGet("/api/test/kafka/consume", () =>
{
    var config = new ConsumerConfig
    {
        BootstrapServers = kafkaBootstrap,
        GroupId = "pam-test-consumer-group",
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = true
    };
    using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
    consumer.Subscribe("pam.test.events");
    var consumeResult = consumer.Consume(TimeSpan.FromSeconds(15));
    if (consumeResult == null)
        return Results.Ok(new { message = "No messages found within timeout window." });

    return Results.Ok(new
    {
        status = "Consumed",
        topic = consumeResult.Topic,
        offset = consumeResult.Offset.Value,
        receivedMessage = consumeResult.Message.Value
    });
});

app.Run();

public partial class Program { }

/// <summary>
/// Writes a structured JSON health report (overall status plus a per-check
/// breakdown) instead of the framework's default plain-text "Healthy"/
/// "Unhealthy" response, so /health genuinely reports individual
/// dependency status (DB, Kafka) rather than just an aggregate string.
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
