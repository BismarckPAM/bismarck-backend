using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuthorizationService.Clients;

public sealed class ResourceServiceClient(
    HttpClient httpClient,
    ILogger<ResourceServiceClient> logger,
    IHttpContextAccessor httpContextAccessor) : IResourceServiceClient
{
    public async Task<ResourceContext?> GetResourceContextAsync(
        Guid resourceId,
        string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/resources/{resourceId}");

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

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogWarning(
                    "Resource Service did not find resource {ResourceId}; authorization must deny.",
                    resourceId);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Resource Service returned {StatusCode} for resource {ResourceId}; authorization must deny.",
                    response.StatusCode,
                    resourceId);
                return null;
            }

            var resource = await response.Content.ReadFromJsonAsync<ResourceResponse>(
                cancellationToken);

            if (resource is null || !resource.IsActive)
                return null;

            if (string.IsNullOrWhiteSpace(resource.Type)
                || string.IsNullOrWhiteSpace(resource.Environment)
                || string.IsNullOrWhiteSpace(resource.Criticality))
                return null;

            return new ResourceContext(
                resource.Type,
                resource.Environment,
                resource.Criticality);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Resource Service is unreachable for resource {ResourceId}; authorization must deny.",
                resourceId);
            return null;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Resource Service timed out for resource {ResourceId}; authorization must deny.",
                resourceId);
            return null;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(
                exception,
                "Resource Service returned an invalid response for resource {ResourceId}; authorization must deny.",
                resourceId);
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

    private sealed record ResourceResponse(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("environment")] string Environment,
        [property: JsonPropertyName("criticality")] string Criticality,
        [property: JsonPropertyName("isActive")] bool IsActive);
}