using System.Collections.Concurrent;

namespace PakiCarpool.Common;

public sealed class RideGroupConnectionTracker
{
    private readonly ConcurrentDictionary<(Guid RideId, Guid PassengerId), ConcurrentDictionary<string, byte>> _connections = new();

    public void Track(Guid rideId, Guid passengerId, string connectionId, CancellationToken connectionAborted)
    {
        if (connectionAborted.IsCancellationRequested)
            return;

        var key = (rideId, passengerId);
        var connectionIds = _connections.GetOrAdd(key, _ => new ConcurrentDictionary<string, byte>());
        connectionIds.TryAdd(connectionId, 0);
        connectionAborted.Register(() => RemoveConnection(connectionId));

        if (connectionAborted.IsCancellationRequested)
            RemoveConnection(connectionId);
    }

    public IReadOnlyCollection<string> GetConnections(Guid rideId, Guid passengerId)
    {
        return _connections.TryGetValue((rideId, passengerId), out var connectionIds)
            ? connectionIds.Keys.ToArray()
            : Array.Empty<string>();
    }

    public void Remove(Guid rideId, Guid passengerId)
    {
        _connections.TryRemove((rideId, passengerId), out _);
    }

    private void RemoveConnection(string connectionId)
    {
        foreach (var entry in _connections)
        {
            entry.Value.TryRemove(connectionId, out _);
            if (entry.Value.IsEmpty)
                _connections.TryRemove(entry.Key, out _);
        }
    }
}