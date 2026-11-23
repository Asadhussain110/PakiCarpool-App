using System.Collections.Concurrent;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.Services.Implementations;

public sealed class InMemoryRideGroupConnectionTracker : IRideGroupConnectionTracker
{
    private readonly ConcurrentDictionary<(Guid RideId, Guid PassengerId), ConcurrentDictionary<string, byte>> _groups = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<(Guid RideId, Guid PassengerId), byte>> _connections = new();

    public Task TrackAsync(Guid rideId, Guid passengerId, string connectionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var group = (rideId, passengerId);
        _groups.GetOrAdd(group, _ => new ConcurrentDictionary<string, byte>()).TryAdd(connectionId, 0);
        _connections.GetOrAdd(connectionId, _ => new ConcurrentDictionary<(Guid, Guid), byte>()).TryAdd(group, 0);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<string>> GetConnectionsAsync(
        Guid rideId,
        Guid passengerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyCollection<string> result = _groups.TryGetValue((rideId, passengerId), out var connectionIds)
            ? connectionIds.Keys.ToArray()
            : Array.Empty<string>();
        return Task.FromResult(result);
    }

    public Task RemoveAsync(Guid rideId, Guid passengerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var group = (rideId, passengerId);
        if (_groups.TryRemove(group, out var connectionIds))
        {
            foreach (var connectionId in connectionIds.Keys)
            {
                if (_connections.TryGetValue(connectionId, out var groups))
                {
                    groups.TryRemove(group, out _);
                    if (groups.IsEmpty)
                        _connections.TryRemove(connectionId, out _);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveConnectionAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_connections.TryRemove(connectionId, out var groups))
        {
            foreach (var group in groups.Keys)
            {
                if (_groups.TryGetValue(group, out var connectionIds))
                {
                    connectionIds.TryRemove(connectionId, out _);
                    if (connectionIds.IsEmpty)
                        _groups.TryRemove(group, out _);
                }
            }
        }

        return Task.CompletedTask;
    }
}