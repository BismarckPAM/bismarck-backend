using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Identity.Service.Exceptions;
using Identity.Service.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Identity.Service.Tests;

public class ExceptionHandlerMiddlewareTests
{
    [Theory]
    [InlineData("validation", 400, "Invalid value")]
    [InlineData("duplicate", 400, "email@example.com")]
    [InlineData("generic", 500, "Unexpected failure")]
    public async Task MapsExceptionsToStructuredErrorResponses(string exceptionType, int expectedStatus, string expectedMessage)
    {
        var middleware = new ExceptionHandlerMiddleware(_ => throw CreateException(exceptionType, expectedMessage), NullLogger<ExceptionHandlerMiddleware>.Instance);
        var context = new DefaultHttpContext
        {
            Response = { Body = new MemoryStream() }
        };

        await middleware.InvokeAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.True(document.RootElement.TryGetProperty("errors", out var errors));
        Assert.True(errors.GetArrayLength() > 0);
        Assert.Contains(expectedMessage, errors[0].GetProperty("ErrorMessage").GetString());
    }

    private static Exception CreateException(string exceptionType, string message)
        => exceptionType switch
        {
            "validation" => new ValidationException(new[] { new ValidationFailure("Name", message) }),
            "duplicate" => new DuplicateEmailException(message),
            _ => new Exception(message)
        };
}
