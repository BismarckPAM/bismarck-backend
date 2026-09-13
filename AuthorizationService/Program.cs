using AuthorizationService.Data;
using AuthorizationService.Clients;
using AuthorizationService.Middleware;
using AuthorizationService.Services;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Extensions.Http;
using Serilog;
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
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<AuthorizationDbContext>("authorization-database");

var app = builder.Build();

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
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

public partial class Program { }
