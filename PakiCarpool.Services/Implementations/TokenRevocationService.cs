using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.Services.Implementations;

public sealed class TokenRevocationService : ITokenRevocationService
{
    private const string KeyPrefix = "revoked-jti";
    private readonly IConnectionMultiplexer? _redis;

    public TokenRevocationService(IConfiguration configuration)
    {
        var connectionString = configuration["Redis:ConnectionString"] ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _redis = null;
            return;
        }

        _redis = ConnectionMultiplexer.Connect(connectionString);
    }

    public async Task RevokeTokenAsync(string jti, TimeSpan? ttl = null)
    {
        if (string.IsNullOrWhiteSpace(jti) || _redis is null)
        {
            return;
        }

        var db = _redis.GetDatabase();
        await db.StringSetAsync($"{KeyPrefix}:{jti}", "1", ttl ?? TimeSpan.FromMinutes(5));
    }

    public async Task<bool> IsRevokedAsync(string jti)
    {
        if (string.IsNullOrWhiteSpace(jti) || _redis is null)
        {
            return false;
        }

        var db = _redis.GetDatabase();
        return await db.KeyExistsAsync($"{KeyPrefix}:{jti}");
    }
}
