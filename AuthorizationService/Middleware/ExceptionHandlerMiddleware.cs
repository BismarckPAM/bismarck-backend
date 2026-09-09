using System.Text.Json;

namespace AuthorizationService.Middleware;

public sealed class ExceptionHandlerMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlerMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled exception while processing request.");
            await WriteErrorResponseAsync(context);
        }
    }

    private static async Task WriteErrorResponseAsync(HttpContext context)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            message = "An unexpected error occurred."
        }));
    }
}