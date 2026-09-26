using System.Net;
using System.Text.Json;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Common;

namespace PakiCarpool.API.Middleware;

/// <summary>
/// Catches all unhandled exceptions and returns a structured error response.
/// Never returns stack traces to the client.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
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
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access on {Path}", context.Request.Path);
            await WriteErrorAsync(context, HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized, ex.Message);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Validation failure on {Path}", context.Request.Path);
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, ErrorCodes.ValidationError, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Path}", context.Request.Path);

            var message = _environment.IsDevelopment()
                ? ex.Message
                : "An unexpected error occurred. Please try again later.";

            await WriteErrorAsync(context, HttpStatusCode.InternalServerError, ErrorCodes.InternalError, message);
        }
    }

    private static async Task WriteErrorAsync(
        HttpContext context,
        HttpStatusCode statusCode,
        string errorCode,
        string message)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.Clear();
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var body = ErrorResponse.Create((int)statusCode, errorCode, message);
        await context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
    }
}
