using System.Net;
using System.Text.Json;

namespace RadioWash.Api.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger, IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            if (!_environment.IsProduction())
            {
                _logger.LogError(ex, "An unhandled exception occurred for {Method} {Path}", 
                    context.Request.Method, context.Request.Path);
            }
            await HandleExceptionAsync(context, ex);
        }
        finally
        {
            // Log all non-success responses
            if (context.Response.StatusCode >= 400)
            {
                _logger.LogWarning(
                    "Request {Method} {Path} returned {StatusCode}",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode);
            }
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Check if response has already been started
        if (context.Response.HasStarted)
        {
            return;
        }

        try
        {
            context.Response.Clear();
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var response = new
            {
                error = "An internal server error occurred",
                details = exception.Message
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
        catch
        {
            // If we can't write the response, there's nothing more we can do
            // The exception has already been logged by the calling method
        }
    }
}
