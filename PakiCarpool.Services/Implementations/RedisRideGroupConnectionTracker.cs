using StackExchange.Redis;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.Services.Implementations;

public sealed class RedisRideGroupConnectionTracker : IRideGroupConnectionTracker
{
    private static readonly TimeSpan MembershipTtl = TimeSpan.FromDays(7);
    private readonly IConnectionMultiplexer _connection;

    public RedisRideGroupConnectionTracker(IConnectionMultiplexer connection)
    {
        _connection = connection;
    }

    public async Task TrackAsync(Guid rideId, Guid passengerId, string connectionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = _connection.GetDatabase();
        var groupKey = GetGroupKey(rideId, passengerId);
        var connectionKey = GetConnectionKey(connectionId);

        await database.SetAddAsync(groupKey, connectionId).WaitAsync(cancellationToken);
        await database.SetAddAsync(connectionKey, groupKey.ToString()).WaitAsync(cancellationToken);
        await database.KeyExpireAsync(groupKey, MembershipTtl).WaitAsync(cancellationToken);
        await database.KeyExpireAsync(connectionKey, MembershipTtl).WaitAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<string>> GetConnectionsAsync(
        Guid rideId,
        Guid passengerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var members = await _connection.GetDatabase()
            .SetMembersAsync(GetGroupKey(rideId, passengerId))
            .WaitAsync(cancellationToken);
        return members.Select(member => member.ToString()).ToArray();
    }

    public async Task RemoveAsync(Guid rideId, Guid passengerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = _connection.GetDatabase();
        var groupKey = GetGroupKey(rideId, passengerId);
        var members = await database.SetMembersAsync(groupKey).WaitAsync(cancellationToken);
        foreach (var member in members)
            await database.SetRemoveAsync(GetConnectionKey(member.ToString()), groupKey.ToString()).WaitAsync(cancellationToken);
        await database.KeyDeleteAsync(groupKey).WaitAsync(cancellationToken);
    }

    public async Task RemoveConnectionAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = _connection.GetDatabase();
        var connectionKey = GetConnectionKey(connectionId);
        var groups = await database.SetMembersAsync(connectionKey).WaitAsync(cancellationToken);
        foreach (var group in groups)
            await database.SetRemoveAsync(group.ToString(), connectionId).WaitAsync(cancellationToken);
        await database.KeyDeleteAsync(connectionKey).WaitAsync(cancellationToken);
    }

    private static RedisKey GetGroupKey(Guid rideId, Guid passengerId)
        => $"ride-group-members:{rideId:D}:{passengerId:D}";

    private static RedisKey GetConnectionKey(string connectionId)
        => $"ride-group-connection:{connectionId}";
}