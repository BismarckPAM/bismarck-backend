using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AuthorizationService.Clients;
using AuthorizationService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AuthorizationService.Tests;

/// <summary>
/// Sprint 2 QA tests for Identity/Resource REST clients and fail-closed behavior.
/// </summary>
public sealed class Sprint2QaAuthorizationClientTests
{
    [Fact]
    public async Task IdentityClient_200_ParsesUserAndExplicitBearerToken()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, $$"""
            {"id":"{{userId}}","roleId":"{{roleId}}","roleName":"Developer","isActive":true}
            """));
        using var httpClient = Client(handler, "https://identity.test/");
        var client = new IdentityServiceClient(
            httpClient,
            NullLogger<IdentityServiceClient>.Instance,
            new HttpContextAccessor());

        var result = await client.GetUserRoleAsync(userId, "explicit-token");

        Assert.NotNull(result.Value);
        Assert.Equal(userId, result.Value!.Id);
        Assert.Equal(roleId, result.Value.RoleId);
        Assert.Equal("Developer", result.Value.RoleName);
        Assert.True(result.Value.IsActive);
        Assert.Null(result.FailureReason);
        Assert.Equal("Bearer", handler.LastAuthorization?.Scheme);
        Assert.Equal("explicit-token", handler.LastAuthorization?.Parameter);
        Assert.Equal($"/api/identity/users/{userId}", handler.LastRequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task IdentityClient_UsesIncomingBearerToken_WhenExplicitTokenMissing()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, $$"""
            {"id":"{{userId}}","roleId":"{{roleId}}","roleName":"Developer","isActive":true}
            """));
        using var httpClient = Client(handler, "https://identity.test/");
        var accessor = ContextWithBearer("incoming-token");
        var client = new IdentityServiceClient(
            httpClient,
            NullLogger<IdentityServiceClient>.Instance,
            accessor);

        await client.GetUserRoleAsync(userId);

        Assert.Equal("incoming-token", handler.LastAuthorization?.Parameter);
    }

    [Fact]
    public async Task IdentityClient_ExplicitToken_OverridesIncomingToken()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, $$"""
            {"id":"{{userId}}","roleId":"{{roleId}}","roleName":"Developer","isActive":true}
            """));
        using var httpClient = Client(handler, "https://identity.test/");
        var client = new IdentityServiceClient(
            httpClient,
            NullLogger<IdentityServiceClient>.Instance,
            ContextWithBearer("incoming-token"));

        await client.GetUserRoleAsync(userId, "explicit-token");

        Assert.Equal("explicit-token", handler.LastAuthorization?.Parameter);
    }

    [Fact]
    public async Task IdentityClient_404_ReturnsUserNotFound()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var httpClient = Client(handler, "https://identity.test/");
        var client = new IdentityServiceClient(
            httpClient,
            NullLogger<IdentityServiceClient>.Instance,
            new HttpContextAccessor());

        var result = await client.GetUserRoleAsync(Guid.NewGuid());

        Assert.Null(result.Value);
        Assert.Equal(AuthorizationDenialReason.USER_NOT_FOUND, result.FailureReason);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task IdentityClient_NonSuccessOtherThan404_FailsClosed(HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(statusCode));
        using var httpClient = Client(handler, "https://identity.test/");
        var client = new IdentityServiceClient(
            httpClient,
            NullLogger<IdentityServiceClient>.Instance,
            new HttpContextAccessor());

        var result = await client.GetUserRoleAsync(Guid.NewGuid());

        Assert.Null(result.Value);
        Assert.Equal(AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED, result.FailureReason);
    }

    [Fact]
    public async Task IdentityClient_HttpRequestException_FailsClosed()
    {
        var handler = new ThrowingHandler(new HttpRequestException("network down"));
        using var httpClient = Client(handler, "https://identity.test/");
        var client = new IdentityServiceClient(
            httpClient,
            NullLogger<IdentityServiceClient>.Instance,
            new HttpContextAccessor());

        var result = await client.GetUserRoleAsync(Guid.NewGuid());

        Assert.Null(result.Value);
        Assert.Equal(AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED, result.FailureReason);
    }

    [Fact]
    public async Task IdentityClient_Timeout_FailsClosed()
    {
        var handler = new ThrowingHandler(new TaskCanceledException("timeout"));
        using var httpClient = Client(handler, "https://identity.test/");
        var client = new IdentityServiceClient(
            httpClient,
            NullLogger<IdentityServiceClient>.Instance,
            new HttpContextAccessor());

        var result = await client.GetUserRoleAsync(Guid.NewGuid());

        Assert.Null(result.Value);
        Assert.Equal(AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED, result.FailureReason);
    }

    [Fact]
    public async Task ResourceClient_200_ParsesActiveResourceAndBearerToken()
    {
        var resourceId = Guid.NewGuid();
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"type":"VM","environment":"DEV","criticality":"LOW","isActive":true}"""));
        using var httpClient = Client(handler, "https://resource.test/");
        var client = new ResourceServiceClient(
            httpClient,
            NullLogger<ResourceServiceClient>.Instance,
            new HttpContextAccessor());

        var result = await client.GetResourceContextAsync(resourceId, "resource-token");

        Assert.NotNull(result.Value);
        Assert.Equal("VM", result.Value!.Type);
        Assert.Equal("DEV", result.Value.Environment);
        Assert.Equal("LOW", result.Value.Criticality);
        Assert.Null(result.FailureReason);
        Assert.Equal("resource-token", handler.LastAuthorization?.Parameter);
        Assert.Equal($"/api/resources/{resourceId}", handler.LastRequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task ResourceClient_UsesIncomingBearerToken_WhenExplicitTokenMissing()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"type":"VM","environment":"DEV","criticality":"LOW","isActive":true}"""));
        using var httpClient = Client(handler, "https://resource.test/");
        var client = new ResourceServiceClient(
            httpClient,
            NullLogger<ResourceServiceClient>.Instance,
            ContextWithBearer("incoming-resource-token"));

        await client.GetResourceContextAsync(Guid.NewGuid());

        Assert.Equal("incoming-resource-token", handler.LastAuthorization?.Parameter);
    }

    [Fact]
    public async Task ResourceClient_404_ReturnsResourceNotFound()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var httpClient = Client(handler, "https://resource.test/");
        var client = new ResourceServiceClient(
            httpClient,
            NullLogger<ResourceServiceClient>.Instance,
            new HttpContextAccessor());

        var result = await client.GetResourceContextAsync(Guid.NewGuid());

        Assert.Null(result.Value);
        Assert.Equal(AuthorizationDenialReason.RESOURCE_NOT_FOUND, result.FailureReason);
    }

    [Fact]
    public async Task ResourceClient_InactiveResource_IsTreatedAsNotFound()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"type":"VM","environment":"DEV","criticality":"LOW","isActive":false}"""));
        using var httpClient = Client(handler, "https://resource.test/");
        var client = new ResourceServiceClient(
            httpClient,
            NullLogger<ResourceServiceClient>.Instance,
            new HttpContextAccessor());

        var result = await client.GetResourceContextAsync(Guid.NewGuid());

        Assert.Null(result.Value);
        Assert.Equal(AuthorizationDenialReason.RESOURCE_NOT_FOUND, result.FailureReason);
    }

    [Theory]
    [InlineData("", "DEV", "LOW")]
    [InlineData("VM", "", "LOW")]
    [InlineData("VM", "DEV", "")]
    public async Task ResourceClient_MissingRequiredContext_FailsClosed(
        string type,
        string environment,
        string criticality)
    {
        var json = JsonSerializer.Serialize(new
        {
            type,
            environment,
            criticality,
            isActive = true
        });
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, json));
        using var httpClient = Client(handler, "https://resource.test/");
        var client = new ResourceServiceClient(
            httpClient,
            NullLogger<ResourceServiceClient>.Instance,
            new HttpContextAccessor());

        var result = await client.GetResourceContextAsync(Guid.NewGuid());

        Assert.Null(result.Value);
        Assert.Equal(AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED, result.FailureReason);
    }

    [Fact]
    public async Task ResourceClient_MalformedJson_FailsClosed()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, "{not-json"));
        using var httpClient = Client(handler, "https://resource.test/");
        var client = new ResourceServiceClient(
            httpClient,
            NullLogger<ResourceServiceClient>.Instance,
            new HttpContextAccessor());

        var result = await client.GetResourceContextAsync(Guid.NewGuid());

        Assert.Null(result.Value);
        Assert.Equal(AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED, result.FailureReason);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task ResourceClient_NonSuccess_FailsClosed(HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(statusCode));
        using var httpClient = Client(handler, "https://resource.test/");
        var client = new ResourceServiceClient(
            httpClient,
            NullLogger<ResourceServiceClient>.Instance,
            new HttpContextAccessor());

        var result = await client.GetResourceContextAsync(Guid.NewGuid());

        Assert.Null(result.Value);
        Assert.Equal(AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED, result.FailureReason);
    }

    [Fact]
    public async Task ResourceClient_HttpRequestException_FailsClosed()
    {
        var handler = new ThrowingHandler(new HttpRequestException("network down"));
        using var httpClient = Client(handler, "https://resource.test/");
        var client = new ResourceServiceClient(
            httpClient,
            NullLogger<ResourceServiceClient>.Instance,
            new HttpContextAccessor());

        var result = await client.GetResourceContextAsync(Guid.NewGuid());

        Assert.Null(result.Value);
        Assert.Equal(AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED, result.FailureReason);
    }

    [Fact]
    public async Task ResourceClient_Timeout_FailsClosed()
    {
        var handler = new ThrowingHandler(new TaskCanceledException("timeout"));
        using var httpClient = Client(handler, "https://resource.test/");
        var client = new ResourceServiceClient(
            httpClient,
            NullLogger<ResourceServiceClient>.Instance,
            new HttpContextAccessor());

        var result = await client.GetResourceContextAsync(Guid.NewGuid());

        Assert.Null(result.Value);
        Assert.Equal(AuthorizationDenialReason.SYSTEM_ERROR_FAIL_CLOSED, result.FailureReason);
    }

    private static HttpClient Client(HttpMessageHandler handler, string baseAddress) =>
        new(handler) { BaseAddress = new Uri(baseAddress) };

    private static HttpContextAccessor ContextWithBearer(string token)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {token}";
        return new HttpContextAccessor { HttpContext = context };
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public AuthenticationHeaderValue? LastAuthorization { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastAuthorization = request.Headers.Authorization;
            LastRequestUri = request.RequestUri;
            return Task.FromResult(responder(request));
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }
}
