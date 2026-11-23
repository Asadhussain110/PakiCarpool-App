using PakiCarpool.Domain.Enums;
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
    /// Atomically accepts a pending booking after locking its ride, rechecking its status,
    /// timeout, ride state, and per-segment capacity in one database transaction.
    /// </summary>
    Task<(BookingAcceptanceOutcome Outcome, Booking? Booking)> AcceptPendingAsync(
        Guid bookingId,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically cancels a booking only if it is still pending and older than the cutoff.
    /// Returns the cancelled booking only to the caller that performed the transition.
    /// </summary>
    Task<Booking?> CancelTimedOutAsync(
        Guid bookingId,
        DateTime cutoff,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all timed-out pending bookings (RequestedAt older than timeout threshold).
    /// Used by BookingTimeoutJob to locate candidates; cancellation is performed conditionally.
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
    /// Checks if all bookings are Completed, Cancelled, or Rejected.
    /// Pending requests and Accepted/Active bookings still block ride completion.
    /// </summary>
    Task<bool> AreAllBookingsCompletedOrCancelledAsync(Guid rideId);
}
