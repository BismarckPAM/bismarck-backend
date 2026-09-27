using System.Text.Json;
using System.Text.Json.Serialization;

namespace Identity.Service.Services;

/// <summary>Server-side verification of a Cloudflare Turnstile token.</summary>
public interface ITurnstileVerifier
{
    bool IsEnabled { get; }

    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken = default);
}

/// <summary>
/// Verifies Turnstile response tokens against Cloudflare's siteverify endpoint.
///
/// Fail-closed: when the verifier is enabled and the token is missing, the
/// request is rejected. The only bypass is the explicit
/// <c>Turnstile:Enabled=false</c> configuration switch for local development.
/// </summary>
public sealed class CloudflareTurnstileVerifier(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<CloudflareTurnstileVerifier> logger) : ITurnstileVerifier
{
    private const string SiteVerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    private readonly string? _secretKey = configuration["Turnstile:SecretKey"];

    public bool IsEnabled { get; } =
        configuration["Turnstile:Enabled"] is { Length: > 0 } enabledConfig
            && bool.TryParse(enabledConfig, out var enabled)
                ? enabled
                : !string.IsNullOrWhiteSpace(configuration["Turnstile:SecretKey"]);

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            logger.LogWarning("Turnstile verification is disabled; accepting token without verification.");
            return true;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Turnstile verification failed: no token supplied.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_secretKey))
        {
            logger.LogError("Turnstile is enabled but no secret key is configured; failing closed.");
            return false;
        }

        try
        {
            var form = new Dictionary<string, string>
            {
                ["secret"] = _secretKey!,
                ["response"] = token!
            };
            if (!string.IsNullOrWhiteSpace(remoteIp))
                form["remoteip"] = remoteIp!;

            using var content = new FormUrlEncodedContent(form);
            using var response = await httpClient.PostAsync(SiteVerifyUrl, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Turnstile siteverify returned HTTP {StatusCode}.", (int)response.StatusCode);
                return false;
            }

            var payload = await response.Content.ReadFromJsonAsync<TurnstileSiteVerifyResponse>(cancellationToken);
            if (payload?.Success == true)
                return true;

            logger.LogWarning(
                "Turnstile verification failed: {Errors}",
                payload?.ErrorCodes is { Count: > 0 } codes ? string.Join(", ", codes) : "unknown error");
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Turnstile siteverify call failed; failing closed.");
            return false;
        }
    }

    private sealed class TurnstileSiteVerifyResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("error-codes")]
        public List<string>? ErrorCodes { get; set; }
    }
}