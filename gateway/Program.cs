
using Gateway.Service.Middleware;
using Prometheus;
using Yarp.ReverseProxy;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Existing internal service destinations.
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

// Monitoring credential must be configured in Azure.
var monitoringToken = builder.Configuration["METRICS_PROXY_TOKEN"];

if (string.IsNullOrWhiteSpace(monitoringToken)
    || monitoringToken.Length < 32)
{
    throw new InvalidOperationException(
        "METRICS_PROXY_TOKEN must contain at least 32 characters.");
}

// Hash the configured credential for constant-time comparison.
var expectedTokenHash = SHA256.HashData(
    Encoding.UTF8.GetBytes(monitoringToken));

var allowedOrigins =
    builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseCors("Frontend");

// Existing gateway HTTP metrics.
app.UseHttpMetrics();

// Dedicated authentication for monitoring proxy endpoints.
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments(
        "/internal/metrics",
        StringComparison.OrdinalIgnoreCase))
    {
        await next(context);
        return;
    }

    if (!HttpMethods.IsGet(context.Request.Method))
    {
        context.Response.StatusCode =
            StatusCodes.Status405MethodNotAllowed;
        return;
    }

    var authorization = context.Request.Headers.Authorization.ToString();

    const string bearerPrefix = "Bearer ";

    if (!authorization.StartsWith(
        bearerPrefix,
        StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode =
            StatusCodes.Status401Unauthorized;
        return;
    }

    var providedToken = authorization[bearerPrefix.Length..].Trim();

    var providedHash = SHA256.HashData(
        Encoding.UTF8.GetBytes(providedToken));

    if (!CryptographicOperations.FixedTimeEquals(
        providedHash,
        expectedTokenHash))
    {
        context.Response.StatusCode =
            StatusCodes.Status401Unauthorized;
        return;
    }

    await next(context);
});

// Normal API requests continue through the original JWT middleware.
// Monitoring requests have already passed dedicated authentication.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments(
        "/internal/metrics",
        StringComparison.OrdinalIgnoreCase),
    branch =>
    {
        branch.UseMiddleware<AuthorizationHeaderValidatorMiddleware>();
    });

app.MapHealthChecks("/health");

// Preserve the existing gateway metrics endpoint.
app.MapMetrics("/metrics");

// Existing routes plus new protected monitoring routes.
app.MapReverseProxy();

app.Run();
