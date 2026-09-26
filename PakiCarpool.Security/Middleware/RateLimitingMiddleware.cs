using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using PakiCarpool.Common.Constants;

namespace PakiCarpool.Security.Middleware;

/// <summary>
/// Rate limiting middleware implementing per-IP request throttling.
/// General endpoints: 100 requests/minute. Auth endpoints: 10 requests/minute.
/// Uses a sliding window approach with concurrent dictionary for thread safety.
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly int _generalLimit;
    private readonly int _authLimit;
    private static readonly ConcurrentDictionary<string, SlidingWindowCounter> Counters = new();

    public RateLimitingMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _generalLimit = configuration.GetValue("RateLimit:GeneralPerMinute", AppConstants.RateLimitGeneralPerMinute);
        _authLimit = configuration.GetValue("RateLimit:AuthPerMinute", AppConstants.RateLimitAuthPerMinute);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        var isAuthEndpoint = path.Contains("/auth/");

        var limit = isAuthEndpoint ? _authLimit : _generalLimit;
        var key = $"{ipAddress}:{(isAuthEndpoint ? "auth" : "general")}";

        var counter = Counters.GetOrAdd(key, _ => new SlidingWindowCounter());
        counter.CleanupExpired();

        if (counter.Count >= limit)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/json";

            var retryAfter = counter.GetRetryAfterSeconds();
            context.Response.Headers.RetryAfter = retryAfter.ToString();

            var response = new
            {
                statusCode = 429,
                error = ErrorCodes.RateLimitExceeded,
                message = $"Rate limit exceeded. Try again in {retryAfter} seconds."
            };

            await context.Response.WriteAsJsonAsync(response);
            return;
        }

        counter.Increment();
        await _next(context);
    }

    /// <summary>
    /// Sliding window counter tracking request timestamps within a 1-minute window.
    /// Thread-safe via lock-based synchronization.
    /// </summary>
    private class SlidingWindowCounter
    {
        private readonly object _lock = new();
        private readonly Queue<DateTime> _timestamps = new();

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _timestamps.Count;
                }
            }
        }

        public void Increment()
        {
            lock (_lock)
            {
                _timestamps.Enqueue(DateTime.UtcNow);
            }
        }

        public void CleanupExpired()
        {
            lock (_lock)
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-1);
                while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
                {
                    _timestamps.Dequeue();
                }
            }
        }

        public int GetRetryAfterSeconds()
        {
            lock (_lock)
            {
                if (_timestamps.Count == 0) return 0;
                var oldest = _timestamps.Peek();
                var retryAfter = oldest.AddMinutes(1) - DateTime.UtcNow;
                return Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            }
        }
    }
}
