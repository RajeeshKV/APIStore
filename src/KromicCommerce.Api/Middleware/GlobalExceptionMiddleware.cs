using System.Diagnostics;
using System.Text.Json;

namespace KromicCommerce.Api.Middleware;

/// <summary>
/// Catches all unhandled exceptions and returns a consistent JSON error envelope.
/// Detailed exception messages are only exposed in Development; production returns a generic message.
/// Never logs request bodies or headers that may contain secrets/tokens.
/// </summary>
internal sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger,
    IHostEnvironment env)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        logger.LogError(exception,
            "Unhandled exception. TraceId: {TraceId} Path: {Path} Method: {Method}",
            traceId, context.Request.Path, context.Request.Method);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var detail = env.IsDevelopment()
            ? exception.Message
            : "An unexpected error occurred. Please try again later.";

        var body = new ErrorResponse(
            Success: false,
            Error: new ErrorDetail(
                Code: "INTERNAL_SERVER_ERROR",
                Message: detail),
            TraceId: traceId);

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(body, JsonOptions));
    }

    private record ErrorResponse(bool Success, ErrorDetail Error, string TraceId);
    private record ErrorDetail(string Code, string Message);
}
