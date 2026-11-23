using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace PakiCarpool.Common;

// Per-instance throttle. State resets on server restart. Revisit if horizontally scaled.
public abstract class ThrottledHub : Hub
{
    private static readonly ConcurrentDictionary<string, RateLimitBucket> _buckets = new();
    private static readonly ConcurrentDictionary<string, TimeSpan> _bucketWindows = new();

    private record struct RateLimitBucket(int Count, DateTime WindowStart);

    /// <summary>
    /// Records a caller's operation in its per-user bucket and reports whether the limit was exceeded.
    /// </summary>
    /// <param name="userId">The authenticated user's identifier.</param>
    /// <param name="bucket">The operation-specific bucket name.</param>
    /// <param name="limit">The maximum allowed operations during the window.</param>
    /// <param name="window">The duration of the rate-limit window.</param>
    /// <returns><see langword="true"/> when the caller has exceeded the limit; otherwise, <see langword="false"/>.</returns>
    protected bool IsThrottled(string userId, string bucket, int limit, TimeSpan window)
    {
        var key = $"{userId}:{bucket}";
        var now = DateTime.UtcNow;
        _bucketWindows.GetOrAdd(bucket, window);

        var throttled = false;
        _buckets.AddOrUpdate(
            key,
            _ =>
            {
                throttled = false;
                return new RateLimitBucket(1, now);
            },
            (_, current) =>
            {
                if (now - current.WindowStart >= window)
                {
                    throttled = false;
                    return new RateLimitBucket(1, now);
                }

                if (current.Count < limit)
                {
                    throttled = false;
                    return current with { Count = current.Count + 1 };
                }

                throttled = true;
                return current;
            });

        return throttled;
    }

    internal static void SweepExpiredBuckets(DateTime now)
    {
        foreach (var entry in _buckets)
        {
            var separatorIndex = entry.Key.IndexOf(':');
            if (separatorIndex < 0 ||
                !_bucketWindows.TryGetValue(entry.Key[(separatorIndex + 1)..], out var window))
            {
                continue;
            }

            if (now <= entry.Value.WindowStart + window + TimeSpan.FromMinutes(1))
                continue;

            ((ICollection<KeyValuePair<string, RateLimitBucket>>)_buckets).Remove(entry);
        }
    }
}