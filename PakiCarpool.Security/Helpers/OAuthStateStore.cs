using System.Collections.Concurrent;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.Security.Helpers;

public sealed class OAuthStateStore : IOAuthStateStore
{
    private readonly ConcurrentDictionary<string, OAuthStateRecord> _states = new();

    public Task<bool> TryCreateAsync(
        string state,
        string role,
        string codeVerifier,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveExpiredStates();
        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(codeVerifier) || ttl <= TimeSpan.Zero)
            return Task.FromResult(false);

        var record = new OAuthStateRecord(role, codeVerifier, DateTime.UtcNow.Add(ttl));
        return Task.FromResult(_states.TryAdd(state, record));
    }

    public Task<OAuthStateRecord?> ConsumeAsync(string state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(state) || !_states.TryRemove(state, out var record))
            return Task.FromResult<OAuthStateRecord?>(null);

        if (record.ExpiresAtUtc <= DateTime.UtcNow)
            return Task.FromResult<OAuthStateRecord?>(null);

        return Task.FromResult<OAuthStateRecord?>(record);
    }

    private void RemoveExpiredStates()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in _states)
        {
            if (entry.Value.ExpiresAtUtc <= now)
                _states.TryRemove(entry.Key, out _);
        }
    }
}
