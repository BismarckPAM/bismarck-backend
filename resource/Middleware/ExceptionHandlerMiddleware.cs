using FluentValidation;
using Resource.Service.Exceptions;
using System.Text.Json;

namespace Resource.Service.Middleware;

public class ExceptionHandlerMiddleware(RequestDelegate next, ILogger<ExceptionHandlerMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException exception)
        {
            await WriteValidationResponseAsync(context, exception, context.RequestAborted);
        }
        catch (NotFoundException exception)
        {
            await WriteErrorResponseAsync(
                context,
                StatusCodes.Status404NotFound,
                exception.Message,
                context.RequestAborted);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled exception while processing request.");
            await WriteErrorResponseAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "An unexpected error occurred.",
                context.RequestAborted);
        }
    }

    private static async Task WriteValidationResponseAsync(
        HttpContext context,
        ValidationException exception,
        CancellationToken cancellationToken)
    {
        var response = new
        {
            errors = exception.Errors.Select(error => new
            {
                propertyName = error.PropertyName,
                errorMessage = error.ErrorMessage
            })
        };

        await WriteJsonResponseAsync(
            context,
            StatusCodes.Status400BadRequest,
            response,
            cancellationToken);
    }

    private static async Task WriteErrorResponseAsync(
        HttpContext context,
        int statusCode,
        string message,
        CancellationToken cancellationToken)
    {
        await WriteJsonResponseAsync(context, statusCode, new { message }, cancellationToken);
    }

    private static async Task WriteJsonResponseAsync(
        HttpContext context,
        int statusCode,
        object response,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(response), cancellationToken);
    }
}
