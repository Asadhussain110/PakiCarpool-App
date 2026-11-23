using System.Text.Json;
using StackExchange.Redis;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.Services.Implementations;

public sealed class RedisOAuthStateStore : IOAuthStateStore
{
    private const string KeyPrefix = "oauth-state:";
    private const string ConsumeScript = "local value = redis.call('GET', KEYS[1]); if value then redis.call('DEL', KEYS[1]); end; return value";
    private readonly IConnectionMultiplexer _connection;

    public RedisOAuthStateStore(IConnectionMultiplexer connection)
    {
        _connection = connection;
    }

    public async Task<bool> TryCreateAsync(
        string state,
        string role,
        string codeVerifier,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(codeVerifier) || ttl <= TimeSpan.Zero)
            return false;

        var record = new OAuthStateRecord(role, codeVerifier, DateTime.UtcNow.Add(ttl));
        var database = _connection.GetDatabase();
        return await database.StringSetAsync(
                KeyPrefix + state,
                JsonSerializer.Serialize(record),
                ttl,
                When.NotExists)
            .WaitAsync(cancellationToken);
    }

    public async Task<OAuthStateRecord?> ConsumeAsync(string state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(state))
            return null;

        var database = _connection.GetDatabase();
        var result = await database.ScriptEvaluateAsync(ConsumeScript, [KeyPrefix + state])
            .WaitAsync(cancellationToken);
        if (result.IsNull)
            return null;

        var record = JsonSerializer.Deserialize<OAuthStateRecord>(result.ToString());
        return record is not null && record.ExpiresAtUtc > DateTime.UtcNow ? record : null;
    }
}