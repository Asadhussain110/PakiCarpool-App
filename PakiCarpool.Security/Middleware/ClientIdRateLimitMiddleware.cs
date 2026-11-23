using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PakiCarpool.Common.Constants;

namespace PakiCarpool.Security.Middleware;

/// <summary>
/// Applies the liveness rate limit per authenticated user ID.
/// </summary>
public class ClientIdRateLimitMiddleware
{
    private const string LivenessChallengePath = "/api/v1/driver/liveness/challenge";
    private const string LivenessSubmitPath = "/api/v1/driver/liveness";
    private const int DefaultLimit = 5;
    private const string DefaultPeriod = "1h";
    private readonly RequestDelegate _next;
    private readonly IRateLimitCounter _counter;
    private readonly ILogger<ClientIdRateLimitMiddleware> _logger;
    private readonly int _limit;
    private readonly TimeSpan _period;

    public ClientIdRateLimitMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IRateLimitCounter counter,
        ILogger<ClientIdRateLimitMiddleware> logger)
    {
        _next = next;
        _counter = counter;
        _logger = logger;
        _limit = configuration.GetValue("RateLimit:Liveness:Limit", DefaultLimit);
        _period = ParsePeriod(configuration["RateLimit:Liveness:Period"] ?? DefaultPeriod);

        if (_limit <= 0 || _period <= TimeSpan.Zero)
            throw new InvalidOperationException("Liveness rate limit values must be positive.");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsLivenessEndpoint(context.Request.Method, context.Request.Path.Value))
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

        var key = $"rate-limit:liveness:{userId}";
        RateLimitCounterResult counterResult;
        try
        {
            counterResult = await _counter.IncrementAsync(key, _period, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Liveness rate-limit counter failed.");
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

    private static bool IsLivenessEndpoint(string method, string? path)
    {
        var normalizedPath = path?.TrimEnd('/');
        return (HttpMethods.IsGet(method)
                && string.Equals(normalizedPath, LivenessChallengePath, StringComparison.OrdinalIgnoreCase))
            || (HttpMethods.IsPost(method)
                && string.Equals(normalizedPath, LivenessSubmitPath, StringComparison.OrdinalIgnoreCase));
    }

    private static TimeSpan ParsePeriod(string period)
    {
        if (period.Length < 2
            || !int.TryParse(period.AsSpan(0, period.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || value <= 0)
        {
            throw new InvalidOperationException("Liveness rate limit period must use a positive value followed by s, m, h, or d.");
        }

        return char.ToLowerInvariant(period[^1]) switch
        {
            's' => TimeSpan.FromSeconds(value),
            'm' => TimeSpan.FromMinutes(value),
            'h' => TimeSpan.FromHours(value),
            'd' => TimeSpan.FromDays(value),
            _ => throw new InvalidOperationException("Liveness rate limit period must use a positive value followed by s, m, h, or d.")
        };
    }
}