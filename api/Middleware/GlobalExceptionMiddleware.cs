using System.Diagnostics;
using System.Net;
using System.Text.Json;
using RadioWash.Api.Services.Exceptions;

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
            // Include the trace ID so a client-reported "internal error with trace abc123" can be
            // grepped straight to the structured log.
            var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
            _logger.LogError(ex, "An unhandled exception occurred for {Method} {Path} (trace {TraceId})",
                context.Request.Method, context.Request.Path, traceId);
            await HandleExceptionAsync(context, ex, traceId);
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

    private async Task HandleExceptionAsync(HttpContext context, Exception exception, string traceId)
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

            // Expected business-rule exception: return a 403 Problem Details body with the
            // limit fields populated so the UI can render a specific message. These fields
            // are the whole purpose of the exception — safe to expose in all environments.
            if (exception is PlanLimitExceededException planLimit)
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                var problem = new
                {
                    type = "https://radiowash.com/problems/plan-limit-exceeded",
                    title = "Plan limit exceeded",
                    status = (int)HttpStatusCode.Forbidden,
                    detail = planLimit.Message,
                    limitType = planLimit.LimitType,
                    limit = planLimit.Limit,
                    current = planLimit.Current,
                    traceId,
                };
                await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
                return;
            }

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            // Only expose exception.Message to clients in Development. In Production/Staging
            // it leaks EF constraint names, SQL, Stripe internals, and occasionally user data
            // through parameter values. Callers get a trace ID instead, which maps back to
            // structured logs and Sentry without exposing internals.
            var response = _environment.IsDevelopment()
                ? (object)new
                {
                    error = "An internal server error occurred",
                    details = exception.Message,
                    traceId,
                }
                : new
                {
                    error = "An internal server error occurred",
                    traceId,
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
