namespace Gateway.Service.Middleware;

/// <summary>
/// Ensures every proxied request carries a well-formed JWT Bearer token before
/// it is forwarded to a downstream service.
///
/// The gateway deliberately does NOT do claims-based authorization — that is the
/// job of each individual service (which has the signing key). It only checks
/// that the Authorization header is present and structurally valid
/// (a JWT is header.payload.signature, three base64url segments). The original
/// header is passed through untouched so the downstream service can validate it.
///
/// Exemptions: CORS preflights (OPTIONS) carry no credentials by design, and
/// paths listed under the "PublicPaths" config section (e.g. the health probe
/// and the login endpoint, which has no token yet). A "PublicPaths" entry may
/// be a bare path ("/health") which exempts every method, or method-qualified
/// ("POST /api/onboarding/tickets") which exempts only that verb.
/// </summary>
public sealed class AuthorizationHeaderValidatorMiddleware
{
    private const string BearerScheme = "Bearer ";

    private readonly RequestDelegate _next;
    private readonly ILogger<AuthorizationHeaderValidatorMiddleware> _logger;

    // Bare paths exempt all methods; method-qualified entries are keyed as "METHOD path".
    private readonly HashSet<string> _publicPaths;
    private readonly HashSet<string> _publicMethodPaths;

    public AuthorizationHeaderValidatorMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<AuthorizationHeaderValidatorMiddleware> logger)
    {
        _next = next;
        _logger = logger;

        var entries = configuration.GetSection("PublicPaths").Get<string[]>() ?? [];
        _publicPaths = entries
            .Where(entry => !entry.Contains(' ', StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        _publicMethodPaths = entries
            .Where(entry => entry.Contains(' ', StringComparison.Ordinal))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Browsers send CORS preflight OPTIONS requests without an Authorization
        // header — let the CORS middleware (which runs before us) decide on those.
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        var methodPath = $"{context.Request.Method} {path}";
        if (_publicPaths.Contains(path) || _publicMethodPaths.Contains(methodPath))
        {
            await _next(context);
            return;
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization)
            || !authorization.StartsWith(BearerScheme, StringComparison.OrdinalIgnoreCase))
        {
            await RejectAsync(context, "Missing or malformed Authorization header.");
            return;
        }

        var token = authorization[BearerScheme.Length..].Trim();
        if (!IsWellFormedJwt(token))
        {
            await RejectAsync(context, "Authorization header is not a well-formed JWT.");
            return;
        }

        await _next(context);
    }

    private static bool IsWellFormedJwt(string token)
    {
        // A structurally valid JWT has exactly three base64url segments.
        var segments = token.Split('.');
        if (segments.Length != 3)
            return false;

        foreach (var segment in segments)
        {
            if (segment.Length == 0)
                return false;

            foreach (var c in segment)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))
                    return false;
            }
        }

        return true;
    }

    private Task RejectAsync(HttpContext context, string message)
    {
        _logger.LogWarning(
            "Rejected request {Method} {Path} from {RemoteIp}: {Message}",
            context.Request.Method,
            context.Request.Path,
            context.Connection.RemoteIpAddress,
            message);

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsJsonAsync(new { message });
    }
}