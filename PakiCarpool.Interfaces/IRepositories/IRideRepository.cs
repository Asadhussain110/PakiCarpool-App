using PakiCarpool.Domain.DTOs.Passenger;
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
    Task<Ride?> GetByIdAsync(Guid rideId, bool includeStops = false, bool includeBookings = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the active (Posted or Active) ride for a driver.
    /// </summary>
    Task<Ride?> GetActiveRideByDriverIdAsync(Guid driverProfileId);

    /// <summary>
    /// Checks whether a user's driver profile has an active ride.
    /// </summary>
    Task<bool> HasActiveRideForUserAsync(Guid userId);

    /// <summary>
    /// Gets the user's most recently created driver ride.
    /// </summary>
    Task<Ride?> GetMostRecentRideForUserAsync(Guid userId);

    /// <summary>
    /// Gets all rides with Posted status (available for booking), with pagination.
    /// </summary>
    Task<(List<Ride> Items, int TotalCount)> GetAvailableRidesPaginatedAsync(int page, int pageSize);

    /// <summary>
    /// Gets active rides and upcoming stop candidates within range of the requested coordinates.
    /// </summary>
    Task<List<RideSearchCandidateDto>> GetCandidateRidesAsync(
        double startLat,
        double startLng,
        double endLat,
        double endLng,
        double radiusMeters);

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
    Task<RideStop?> GetStopByIdAsync(Guid stopId, CancellationToken cancellationToken = default);

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
