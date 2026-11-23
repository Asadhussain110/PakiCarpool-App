using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PakiCarpool.Common.Constants;

namespace PakiCarpool.Security.Middleware;

/// <summary>
/// Applies shared fixed-window limits by client IP and request category.
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRateLimitCounter _counter;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly IConfiguration _configuration;
    private readonly int _generalLimit;
    private readonly int _authLimit;

    public RateLimitingMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IRateLimitCounter counter,
        ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _counter = counter;
        _logger = logger;
        _generalLimit = Math.Clamp(
            configuration.GetValue("RateLimit:GeneralPerMinute", AppConstants.RateLimitGeneralPerMinute),
            1,
            AppConstants.RateLimitGeneralPerMinute);
        _authLimit = Math.Clamp(
            configuration.GetValue("RateLimit:AuthPerMinute", AppConstants.RateLimitAuthPerMinute),
            1,
            AppConstants.RateLimitAuthPerMinute);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var category = GetCategory(context.Request);
        var ipAddress = context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "unknown";
        RateLimitCounterResult counterResult;
        try
        {
            counterResult = await _counter.IncrementAsync(
                $"rate-limit:ip:{ipAddress}:{category.Name}",
                category.Window,
                context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Rate-limit counter failed for category {Category}.", category.Name);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                statusCode = StatusCodes.Status503ServiceUnavailable,
                error = "RATE_LIMITER_UNAVAILABLE",
                message = "Request protection is temporarily unavailable."
            }, context.RequestAborted);
            return;
        }

        if (counterResult.Count > category.Limit)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/json";
            context.Response.Headers.RetryAfter = counterResult.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await context.Response.WriteAsJsonAsync(new
            {
                statusCode = StatusCodes.Status429TooManyRequests,
                error = ErrorCodes.RateLimitExceeded,
                message = "Rate limit exceeded. Try again later."
            }, context.RequestAborted);
            return;
        }

        await _next(context);
    }

    private (string Name, int Limit, TimeSpan Window) GetCategory(HttpRequest request)
    {
        if (request.Path.StartsWithSegments("/api/v1/auth/admin/forgot-password"))
        {
            return (
                "admin-password-reset-request",
                Math.Max(1, _configuration.GetValue("RateLimit:AdminPasswordResetRequestLimit", 3)),
                TimeSpan.FromMinutes(15));
        }

        if (request.Path.StartsWithSegments("/api/v1/auth/admin/reset-password"))
        {
            return (
                "admin-password-reset-complete",
                Math.Max(1, _configuration.GetValue("RateLimit:AdminPasswordResetCompleteLimit", 8)),
                TimeSpan.FromMinutes(15));
        }

        if (request.Path.StartsWithSegments("/api/v1/auth"))
            return ("auth", _authLimit, TimeSpan.FromMinutes(1));

        var livenessPath = request.Path.StartsWithSegments("/api/v1/driver/liveness");
        if (livenessPath)
        {
            var limit = Math.Max(1, _configuration.GetValue("RateLimit:Liveness:Limit", 5));
            var period = ParsePeriod(_configuration["RateLimit:Liveness:Period"]);
            return ("liveness", limit, period);
        }

        if (request.Path.StartsWithSegments("/api/v1/driver/location"))
        {
            var limit = Math.Max(1, _configuration.GetValue("RateLimit:Location:PerMinute", 30));
            return ("location", limit, TimeSpan.FromMinutes(1));
        }

        return ("general", _generalLimit, TimeSpan.FromMinutes(1));
    }

    private static TimeSpan ParsePeriod(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return TimeSpan.FromHours(1);

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.EndsWith('h') && double.TryParse(normalized[..^1], out var hours))
            return TimeSpan.FromHours(Math.Clamp(hours, 1, 24));
        if (normalized.EndsWith('m') && double.TryParse(normalized[..^1], out var minutes))
            return TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 1440));
        if (TimeSpan.TryParse(value, out var parsed) && parsed > TimeSpan.Zero)
            return parsed;

        return TimeSpan.FromHours(1);
    }
}
