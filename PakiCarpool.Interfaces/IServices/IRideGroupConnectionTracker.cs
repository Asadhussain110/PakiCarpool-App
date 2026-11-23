namespace PakiCarpool.Interfaces.IServices;

public interface IRideGroupConnectionTracker
{
    Task TrackAsync(Guid rideId, Guid passengerId, string connectionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<string>> GetConnectionsAsync(Guid rideId, Guid passengerId, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid rideId, Guid passengerId, CancellationToken cancellationToken = default);
    Task RemoveConnectionAsync(string connectionId, CancellationToken cancellationToken = default);
}