using System.Net;
using Gateway.Service.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gateway.Service.Tests;

public sealed class AuthorizationHeaderValidatorMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AllowsOptionsRequestsWithoutToken()
    {
        var context = CreateContext("OPTIONS", "/api/private");
        var nextCalled = false;

        await CreateMiddleware(() => nextCalled = true).InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/health", "GET")]
    [InlineData("/api/onboarding/tickets", "POST")]
    [InlineData("/swagger/index.html", "GET")]
    public async Task InvokeAsync_AllowsConfiguredPublicRequests(string path, string method)
    {
        var context = CreateContext(method, path);
        var nextCalled = false;

        await CreateMiddleware(() => nextCalled = true, "/health", "POST /api/onboarding/tickets")
            .InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_RejectsMissingAuthorizationHeader()
    {
        var context = CreateContext("GET", "/api/private");

        await CreateMiddleware().InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Contains("Missing or malformed", await ReadBodyAsync(context));
    }

    [Theory]
    [InlineData("Basic abc", "Missing or malformed")]
    [InlineData("Bearer one.two", "well-formed JWT")]
    [InlineData("Bearer one.two.three!", "well-formed JWT")]
    public async Task InvokeAsync_RejectsMalformedBearerTokens(
        string authorization,
        string expectedMessage)
    {
        var context = CreateContext("GET", "/api/private");
        context.Request.Headers.Authorization = authorization;

        await CreateMiddleware().InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Contains(expectedMessage, await ReadBodyAsync(context));
    }

    [Fact]
    public async Task InvokeAsync_AllowsWellFormedBearerToken()
    {
        var context = CreateContext("GET", "/api/private");
        context.Request.Headers.Authorization = "Bearer eyJhbGciOiJub25lIn0.eyJzdWIiOiIxIn0.signature";
        var nextCalled = false;

        await CreateMiddleware(() => nextCalled = true).InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_UsesQueryTokenForTerminalWebSocketRequest()
    {
        var context = CreateContext("GET", "/api/jit/terminal/session");
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpWebSocketFeature>(
            new TestWebSocketFeature());
        context.Request.QueryString = new QueryString("?access_token=one.two.three");
        var nextCalled = false;

        await CreateMiddleware(() => nextCalled = true).InvokeAsync(context);

        Assert.True(nextCalled);
    }

    private static AuthorizationHeaderValidatorMiddleware CreateMiddleware(
        Action? onNext = null,
        params string[] publicPaths)
    {
        var settings = publicPaths
            .Select((path, index) => new KeyValuePair<string, string?>($"PublicPaths:{index}", path))
            .ToDictionary(item => item.Key, item => item.Value);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new(
            _ =>
            {
                onNext?.Invoke();
                return Task.CompletedTask;
            },
            configuration,
            NullLogger<AuthorizationHeaderValidatorMiddleware>.Instance);
    }

    private static DefaultHttpContext CreateContext(string method, string path)
        => new()
        {
            Request =
            {
                Method = method,
                Path = path,
                Body = Stream.Null
            },
            Response = { Body = new MemoryStream() },
            Connection = { RemoteIpAddress = IPAddress.Loopback }
        };

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }

    private sealed class TestWebSocketFeature : Microsoft.AspNetCore.Http.Features.IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;

        public Task<System.Net.WebSockets.WebSocket> AcceptAsync(
            Microsoft.AspNetCore.Http.WebSocketAcceptContext context) =>
            throw new NotSupportedException();
    }
}
