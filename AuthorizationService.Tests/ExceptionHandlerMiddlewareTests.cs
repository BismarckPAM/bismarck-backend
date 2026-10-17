using System.Text.Json;
using AuthorizationService.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AuthorizationService.Tests;

public sealed class ExceptionHandlerMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ConvertsUnhandledExceptionToInternalServerError()
    {
        var middleware = new ExceptionHandlerMiddleware(
            _ => throw new InvalidOperationException("failure"),
            NullLogger<ExceptionHandlerMiddleware>.Instance);
        var context = new DefaultHttpContext
        {
            Response = { Body = new MemoryStream() }
        };

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(
            "An unexpected error occurred.",
            document.RootElement.GetProperty("message").GetString());
    }

}
