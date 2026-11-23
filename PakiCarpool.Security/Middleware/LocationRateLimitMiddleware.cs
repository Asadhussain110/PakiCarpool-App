using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PakiCarpool.Common.Constants;

namespace PakiCarpool.Security.Middleware;

/// <summary>
/// Applies the driver location REST rate limit per authenticated user ID.
/// </summary>
public sealed class LocationRateLimitMiddleware
{
    private const string LocationPath = "/api/v1/driver/location";
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(1);

    private readonly RequestDelegate _next;
    private readonly IRateLimitCounter _counter;
    private readonly ILogger<LocationRateLimitMiddleware> _logger;
    private readonly int _limit;

    public LocationRateLimitMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IRateLimitCounter counter,
        ILogger<LocationRateLimitMiddleware> logger)
    {
        _next = next;
        _counter = counter;
        _logger = logger;
        _limit = configuration.GetValue<int>("RateLimit:Location:PerMinute");

        if (_limit <= 0)
            throw new InvalidOperationException("Location rate limit must be positive.");
    }

    /// <summary>
    /// Enforces the location limit for the authenticated user and forwards requests within quota.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method)
            || !string.Equals(
                context.Request.Path.Value?.TrimEnd('/'),
                LocationPath,
                StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var userId = context.User.FindFirst("userId")?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
        {
            await _next(context);
            return;
        }

        var key = $"rate-limit:location:{userId}";
        RateLimitCounterResult counterResult;
        try
        {
            counterResult = await _counter.IncrementAsync(key, Period, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Driver location rate-limit counter failed.");
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

        if (counterResult.Count > _limit)
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
}