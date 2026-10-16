
using AuthorizationService.Data;
using AuthorizationService.Clients;
using AuthorizationService.Middleware;
using AuthorizationService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Polly;
using Polly.Extensions.Http;
using Serilog;
using Prometheus;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();

// JWT Authentication & Authorization must match Identity's token contract.
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

        // WebSocket JWT handling for the JIT terminal.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token))
                {
                    var queryToken = context.Request.Query["access_token"].ToString();
                    if (!string.IsNullOrWhiteSpace(queryToken))
                        context.Token = queryToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var identityServiceBaseUrl = Environment.GetEnvironmentVariable("IDENTITY_SERVICE_URL")
    ?? builder.Configuration["IdentityService:BaseUrl"]
    ?? throw new InvalidOperationException(
        "IdentityService:BaseUrl or IDENTITY_SERVICE_URL is required.");

if (!Uri.TryCreate(identityServiceBaseUrl, UriKind.Absolute, out var identityServiceUri))
    throw new InvalidOperationException(
        "IdentityService:BaseUrl must be an absolute URL.");

var retryPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(2, retryAttempt => TimeSpan.FromMilliseconds(100 * retryAttempt));
var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(3));

builder.Services
    .AddHttpClient<IIdentityServiceClient, IdentityServiceClient>(client =>
    {
        client.BaseAddress = identityServiceUri;
        client.Timeout = TimeSpan.FromSeconds(5);
    })
    .AddPolicyHandler(retryPolicy)
    .AddPolicyHandler(timeoutPolicy);

var resourceServiceBaseUrl = Environment.GetEnvironmentVariable("RESOURCE_SERVICE_URL")
    ?? builder.Configuration["ResourceService:BaseUrl"]
    ?? throw new InvalidOperationException(
        "ResourceService:BaseUrl or RESOURCE_SERVICE_URL is required.");

if (!Uri.TryCreate(resourceServiceBaseUrl, UriKind.Absolute, out var resourceServiceUri))
    throw new InvalidOperationException(
        "ResourceService:BaseUrl must be an absolute URL.");

builder.Services
    .AddHttpClient<IResourceServiceClient, ResourceServiceClient>(client =>
    {
        client.BaseAddress = resourceServiceUri;
        client.Timeout = TimeSpan.FromSeconds(5);
    })
    .AddPolicyHandler(retryPolicy)
    .AddPolicyHandler(timeoutPolicy);

var connectionString = builder.Configuration.GetConnectionString("AuthorizationDatabase")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:AuthorizationDatabase is required.");

builder.Services.AddDbContext<AuthorizationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IPolicyDecisionEngine, PolicyDecisionEngine>();
builder.Services.AddSingleton<IAuthorizationEventPublisher, KafkaAuthorizationEventPublisher>();

builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<AuthorizationDbContext>("authorization-database");

builder.Services.AddSingleton<ISystemClock, SystemClock>();

// Azure VM JIT provisioning.
builder.Services.AddSingleton<Azure.Core.TokenCredential>(sp =>
    new Azure.Identity.DefaultAzureCredential(
        new Azure.Identity.DefaultAzureCredentialOptions
        {
            ExcludeInteractiveBrowserCredential = true,
            ExcludeAzureCliCredential = true
        }));

builder.Services.AddHttpClient<AzureJitProvisioner>();
builder.Services.AddTransient<IAzureJitProvisioner>(sp =>
{
    var options = AzureJitOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>());
    return options.IsConfigured
        ? sp.GetRequiredService<AzureJitProvisioner>()
        : new NoOpJitProvisioner(sp.GetRequiredService<ILogger<NoOpJitProvisioner>>());
});

// Brokered SSH terminal and background workers.
builder.Services.Configure<JitSshOptions>(builder.Configuration.GetSection(JitSshOptions.SectionName));
builder.Services.AddSingleton<IJitTerminalBroker, JitTerminalBroker>();

builder.Services.AddHostedService<ApprovalGrantedConsumer>();
builder.Services.AddHostedService<TemporaryPermissionExpirationWorker>();

var app = builder.Build();


if (app.Environment.IsDevelopment()
    || app.Environment.IsStaging()
    || app.Environment.IsEnvironment("Testing")
    || app.Configuration.GetValue<bool>("Swagger:Enabled"))
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

// Prometheus HTTP instrumentation.
app.UseHttpMetrics();

// Authentication and authorization.
app.UseAuthentication();
app.UseAuthorization();

// Existing endpoints.
app.MapHealthChecks("/health");
app.MapControllers();

// Prometheus scrape endpoint.
app.MapMetrics("/metrics");

await app.RunAsync();

public partial class Program { }
