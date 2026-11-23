using System.Collections.Concurrent;

namespace PakiCarpool.Security.Middleware;

public sealed record RateLimitCounterResult(long Count, int RetryAfterSeconds);

public interface IRateLimitCounter
{
    Task<RateLimitCounterResult> IncrementAsync(
        string key,
        TimeSpan window,
        CancellationToken cancellationToken = default);
}

public sealed class InMemoryRateLimitCounter : IRateLimitCounter
{
    private readonly ConcurrentDictionary<string, CounterWindow> _counters = new();
    private long _calls;

    public Task<RateLimitCounterResult> IncrementAsync(
        string key,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var counter = _counters.GetOrAdd(key, _ => new CounterWindow());
        var now = DateTimeOffset.UtcNow;
        RateLimitCounterResult result;

        lock (counter.SyncRoot)
        {
            if (counter.ExpiresAt <= now)
            {
                counter.Count = 0;
                counter.ExpiresAt = now.Add(window);
            }

            counter.Count++;
            result = new RateLimitCounterResult(
                counter.Count,
                Math.Max(1, (int)Math.Ceiling((counter.ExpiresAt - now).TotalSeconds)));
        }

        if (Interlocked.Increment(ref _calls) % 1024 == 0)
        {
            foreach (var entry in _counters)
            {
                if (entry.Value.ExpiresAt <= now)
                    _counters.TryRemove(entry.Key, out _);
            }
        }

        return Task.FromResult(result);
    }

    private sealed class CounterWindow
    {
        public object SyncRoot { get; } = new();
        public long Count { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }
}