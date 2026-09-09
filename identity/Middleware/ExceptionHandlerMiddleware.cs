using FluentValidation;
using Identity.Service.Exceptions;
using System.Net;
using System.Text.Json;

namespace Identity.Service.Middleware;

public class ExceptionHandlerMiddleware(RequestDelegate next, ILogger<ExceptionHandlerMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled request exception");
            await WriteErrorResponseAsync(context, exception);
        }
    }

    private static async Task WriteErrorResponseAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = exception switch
        {
            ValidationException => (int)HttpStatusCode.BadRequest,
            DuplicateEmailException => (int)HttpStatusCode.BadRequest,
            NotFoundException => (int)HttpStatusCode.NotFound,
            _ => (int)HttpStatusCode.InternalServerError
        };

        var message = exception is ValidationException validationException
            ? validationException.Errors.Select(error => new { error.PropertyName, error.ErrorMessage })
            : new[] { new { PropertyName = string.Empty, ErrorMessage = exception.Message } };

        await context.Response.WriteAsync(JsonSerializer.Serialize(new { errors = message }));
    }
}
