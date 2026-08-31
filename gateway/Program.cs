using Gateway.Service.Middleware;
using Yarp.ReverseProxy;

var builder = WebApplication.CreateBuilder(args);

// Internal service URLs come from configuration, never hardcoded. Each entry in
// the "Services" config section (overridable via env vars such as
// Services__Identity__Url) is mapped onto the YARP cluster destination for
// cluster "{name}-cluster" / destination "{name}". This lets the same image run
// against localhost, Docker Compose service names, or Azure Container Apps
// internal URLs without a rebuild.
foreach (var service in builder.Configuration.GetSection("Services").GetChildren())
{
    var name = service.Key;
    var url = service["Url"];
    if (string.IsNullOrWhiteSpace(url))
        continue;

    builder.Configuration[$"ReverseProxy:Clusters:{name}-cluster:Destinations:{name}:Address"] = url;
}

// CORS is only relevant at the gateway: browser clients (the React SPA) call
// here, while internal service-to-service calls go direct and are not subject
// to CORS (architecture rule #4). Origins are read from AllowedOrigins config.
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// YARP reverse proxy. Routes and clusters live in the "ReverseProxy" config
// section — adding a downstream service is a config-only change (one route +
// one cluster + one Services:Url entry), no code change.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Liveness probe for container orchestrators (Azure Container Apps).
builder.Services.AddHealthChecks();

var app = builder.Build();

// Order matters: CORS must run before the proxy so preflights and cross-origin
// responses are handled, then the gateway's own auth-header gate, then YARP.
app.UseCors("Frontend");
app.UseMiddleware<AuthorizationHeaderValidatorMiddleware>();
app.MapHealthChecks("/health");
app.MapReverseProxy();

app.Run();
