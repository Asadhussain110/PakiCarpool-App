using PakiCarpool.Domain.Models;

namespace PakiCarpool.Interfaces.IRepositories;

/// <summary>
/// Repository for Booking entity operations.
/// Handles booking lifecycle, seat capacity checks, and timeout queries.
/// </summary>
public interface IBookingRepository
{
    /// <summary>
    /// Gets a booking by ID, optionally including related entities.
    /// </summary>
    Task<Booking?> GetByIdAsync(Guid bookingId, bool includeRide = false, bool includeStops = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the active booking for a passenger (Pending, Accepted, or Active status).
    /// </summary>
    Task<Booking?> GetActiveBookingByPassengerIdAsync(Guid passengerId);

    /// <summary>
    /// Gets all bookings for a ride.
    /// </summary>
    Task<List<Booking>> GetBookingsByRideIdAsync(Guid rideId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a paginated page of bookings for a ride.
    /// </summary>
    Task<(List<Booking> Items, int TotalCount)> GetBookingsByRideIdAsync(Guid rideId, int page, int pageSize);

    /// <summary>
    /// Gets all timed-out pending bookings (RequestedAt older than timeout threshold).
    /// Used by BookingTimeoutJob.
    /// </summary>
    Task<List<Booking>> GetTimedOutPendingBookingsAsync(DateTime cutoff);

    /// <summary>
    /// Creates a new booking.
    /// </summary>
    Task<Booking> CreateAsync(Booking booking);

    /// <summary>
    /// Updates an existing booking.
    /// </summary>
    Task UpdateAsync(Booking booking);

    /// <summary>
    /// Checks seat capacity for a specific segment using Dapper.
    /// Returns the count of overlapping accepted/active bookings.
    /// </summary>
    Task<int> GetOverlappingBookingCountAsync(Guid rideId, int requestedPickupOrder, int requestedDropoffOrder);

    /// <summary>
    /// Checks if all bookings for a ride are completed or cancelled.
    /// Used to determine if ride should be marked as Completed.
    /// </summary>
    Task<bool> AreAllBookingsCompletedOrCancelledAsync(Guid rideId);
}
