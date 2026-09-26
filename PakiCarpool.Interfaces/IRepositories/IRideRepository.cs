using PakiCarpool.Domain.Models;

namespace PakiCarpool.Interfaces.IRepositories;

/// <summary>
/// Repository for Ride, RideStop, and DriverLocation operations.
/// Handles ride lifecycle, stop progression, and real-time location tracking.
/// </summary>
public interface IRideRepository
{
    /// <summary>
    /// Gets a ride by ID, optionally including stops and bookings.
    /// </summary>
    Task<Ride?> GetByIdAsync(Guid rideId, bool includeStops = false, bool includeBookings = false);

    /// <summary>
    /// Gets the active (Posted or Active) ride for a driver.
    /// </summary>
    Task<Ride?> GetActiveRideByDriverIdAsync(Guid driverProfileId);

    /// <summary>
    /// Gets all rides with Posted status (available for booking), with pagination.
    /// </summary>
    Task<(List<Ride> Items, int TotalCount)> GetAvailableRidesPaginatedAsync(int page, int pageSize);

    /// <summary>
    /// Gets all rides with pagination (admin view).
    /// </summary>
    Task<(List<Ride> Items, int TotalCount)> GetAllPaginatedAsync(int page, int pageSize);

    /// <summary>
    /// Creates a new ride with its stops.
    /// </summary>
    Task<Ride> CreateAsync(Ride ride);

    /// <summary>
    /// Updates an existing ride.
    /// </summary>
    Task UpdateAsync(Ride ride);

    /// <summary>
    /// Gets a ride stop by ID.
    /// </summary>
    Task<RideStop?> GetStopByIdAsync(Guid stopId);

    /// <summary>
    /// Updates a ride stop (e.g., marking as completed).
    /// </summary>
    Task UpdateStopAsync(RideStop stop);

    /// <summary>
    /// Upserts a driver location record (one per driver per ride).
    /// </summary>
    Task UpsertDriverLocationAsync(DriverLocation location);

    /// <summary>
    /// Gets the latest driver location for a ride.
    /// </summary>
    Task<DriverLocation?> GetDriverLocationAsync(Guid driverProfileId, Guid rideId);

    /// <summary>
    /// Gets the count of total rides.
    /// </summary>
    Task<int> CountTotalRidesAsync();

    /// <summary>
    /// Gets the count of active rides.
    /// </summary>
    Task<int> CountActiveRidesAsync();

    /// <summary>
    /// Gets all rides where grace period has expired but status is still Posted.
    /// Used by RideGraceExpiryJob.
    /// </summary>
    Task<List<Ride>> GetExpiredGracePeriodRidesAsync();
}
