using Gateway.Service.Middleware;
using Prometheus;
using Yarp.ReverseProxy;

var builder = WebApplication.CreateBuilder(args);

// Internal service URLs come from configuration.
// Supports environment variable overrides such as Services__Identity__Url.
foreach (var service in builder.Configuration.GetSection("Services").GetChildren())
{
    var name = service.Key;
    var url = service["Url"];

    if (string.IsNullOrWhiteSpace(url))
        continue;

    builder.Configuration[
        $"ReverseProxy:Clusters:{name}-cluster:Destinations:{name}:Address"
    ] = url;
}

// CORS configuration for the frontend.
var allowedOrigins =
    builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// YARP reverse proxy.
builder.Services.AddReverseProxy()
    .LoadFromConfig(
        builder.Configuration.GetSection("ReverseProxy"));

// Health checks for container orchestration.
builder.Services.AddHealthChecks();

var app = builder.Build();

// CORS must run before the authorization-header middleware.
app.UseCors("Frontend");

// Collect HTTP request metrics for Prometheus.
app.UseHttpMetrics();

// Validate JWT headers on protected requests.
app.UseMiddleware<AuthorizationHeaderValidatorMiddleware>();

// Health endpoint.
app.MapHealthChecks("/health");

// Prometheus metrics endpoint.
app.MapMetrics("/metrics");

// Reverse proxy routes.
app.MapReverseProxy();

app.Run();
