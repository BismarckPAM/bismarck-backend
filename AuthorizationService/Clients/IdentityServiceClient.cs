using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AuthorizationService.Clients;

public sealed class IdentityServiceClient(
    HttpClient httpClient,
    ILogger<IdentityServiceClient> logger,
    IHttpContextAccessor httpContextAccessor) : IIdentityServiceClient
{
    public async Task<IdentityUser?> GetUserRoleAsync(
        Guid userId,
        string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/identity/users/{userId}");

        var bearerToken = accessToken ?? GetIncomingBearerToken();
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
            bearerToken);
        }

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode is HttpStatusCode.NotFound
                or HttpStatusCode.InternalServerError)
            {
                logger.LogWarning(
                    "Identity Service returned {StatusCode} for user {UserId}; treating the user as unverified.",
                    response.StatusCode,
                    userId);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Identity Service returned unexpected status {StatusCode} for user {UserId}; treating the user as unverified.",
                    response.StatusCode,
                    userId);
                return null;
            }

            var user = await response.Content.ReadFromJsonAsync<IdentityUserResponse>(
                cancellationToken);

            return user is null
                ? null
                : new IdentityUser(user.Id, user.RoleId, user.RoleName, user.IsActive);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Identity Service is unreachable for user {UserId}; treating the user as unverified.",
                userId);
            return null;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Identity Service timed out for user {UserId}; treating the user as unverified.",
                userId);
            return null;
        }
    }

    private string? GetIncomingBearerToken()
    {
        var authorization = httpContextAccessor.HttpContext?
            .Request.Headers.Authorization.ToString();

        return authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? authorization["Bearer ".Length..].Trim()
            : null;
    }

    private sealed record IdentityUserResponse(
        Guid Id,
        Guid RoleId,
        [property: JsonPropertyName("roleName")] string RoleName,
        bool IsActive);
}