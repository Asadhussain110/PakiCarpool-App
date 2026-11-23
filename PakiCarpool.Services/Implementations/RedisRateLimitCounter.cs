using StackExchange.Redis;
using PakiCarpool.Security.Middleware;

namespace PakiCarpool.Services.Implementations;

public sealed class RedisRateLimitCounter : IRateLimitCounter
{
    private const string IncrementScript = "local count = redis.call('INCR', KEYS[1]); if count == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]); end; return {count, redis.call('PTTL', KEYS[1])}";
    private readonly IConnectionMultiplexer _connection;

    public RedisRateLimitCounter(IConnectionMultiplexer connection)
    {
        _connection = connection;
    }

    public async Task<RateLimitCounterResult> IncrementAsync(
        string key,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scriptResult = await _connection.GetDatabase()
            .ScriptEvaluateAsync(
                IncrementScript,
                [key],
                [(long)window.TotalMilliseconds])
            .WaitAsync(cancellationToken);

        if (scriptResult.IsNull)
            throw new InvalidOperationException("Redis did not return a rate-limit counter result.");

        var values = (RedisResult[]?)scriptResult;
        if (values is null || values.Length != 2)
            throw new InvalidOperationException("Redis returned an invalid rate-limit counter result.");

        var count = (long)values[0];
        var remainingMilliseconds = (long)values[1];
        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(remainingMilliseconds / 1000d));
        return new RateLimitCounterResult(count, retryAfterSeconds);
    }
}