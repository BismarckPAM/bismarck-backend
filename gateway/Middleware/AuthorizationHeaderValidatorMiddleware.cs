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

    /// <summary>
    /// The brokered-terminal route, the one place a token may arrive as a query
    /// parameter because a WebSocket handshake cannot carry a header.
    /// </summary>
    private const string TerminalPathPrefix = "/api/jit/terminal";

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

        var token = ResolveToken(context, path);
        if (string.IsNullOrWhiteSpace(token))
        {
            await RejectAsync(context, "Missing or malformed Authorization header.");
            return;
        }

        if (!IsWellFormedJwt(token))
        {
            await RejectAsync(context, "Authorization header is not a well-formed JWT.");
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Extracts the bearer token.
    ///
    /// WebSocket handshakes are the exception: the browser WebSocket API cannot
    /// set an Authorization header, so the token has to travel as the
    /// <c>access_token</c> query parameter on the terminal endpoint. Without this
    /// every brokered-terminal connection was rejected 401 before it ever reached
    /// the Authorization Service. Only the brokered terminal route opts in, so
    /// this does not weaken the header requirement everywhere else.
    /// </summary>
    private static string? ResolveToken(HttpContext context, string path)
    {
        if (context.WebSockets.IsWebSocketRequest
            && path.StartsWith(TerminalPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var fromQuery = context.Request.Query["access_token"].ToString();
            if (!string.IsNullOrWhiteSpace(fromQuery))
                return fromQuery.Trim();
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization)
            || !authorization.StartsWith(BearerScheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return authorization[BearerScheme.Length..].Trim();
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