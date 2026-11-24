using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.DTOs.Driver;
using PakiCarpool.Domain.DTOs.Passenger;

namespace PakiCarpool.Interfaces.IServices;

/// <summary>
/// Defines all passenger-facing business operations.
/// Covers ride browsing, booking lifecycle, and no-show reporting.
/// </summary>
public interface IPassengerService
{
    /// <summary>
    /// Lists all available (Posted) rides with stops and driver info.
    /// </summary>
    Task<ApiResponse<PaginatedResult<RideResponseDto>>> GetAvailableRidesAsync(PaginationParams pagination);

    /// <summary>
    /// Returns detailed information about a specific ride including stops and available capacity.
    /// </summary>
    Task<ApiResponse<RideResponseDto>> GetRideDetailAsync(Guid rideId);

    /// <summary>
    /// Creates a booking request. Validates: no active booking, segment capacity, pickup stop not passed.
    /// Starts 2-minute timeout via background job.
    /// </summary>
    Task<ApiResponse<object>> CreateBookingAsync(Guid passengerId, CreateBookingDto dto);

    /// <summary>
    /// Cancels a booking after acceptance. Requires reason (min 10 chars).
    /// Classifies cancellation for trust scoring.
    /// </summary>
    Task<ApiResponse<object>> CancelBookingAsync(Guid passengerId, Guid bookingId, CancelBookingDto dto);

    /// <summary>
    /// Returns the passenger's currently active booking with driver and ride details.
    /// </summary>
    Task<ApiResponse<ActiveBookingResponseDto>> GetActiveBookingAsync(Guid passengerId);

    /// <summary>
    /// Records a driver no-show after the 3-minute wait from booking acceptance, when the
    /// driver has not arrived at the pickup stop and is not visibly en route. Persists the
    /// NoShowLog, deducts 10 trust points from the driver, and cancels the booking atomically.
    /// The passenger receives no penalty. One report per booking.
    /// </summary>
    Task<ApiResponse<object>> MarkDriverNoShowAsync(Guid passengerId, Guid bookingId, CancellationToken cancellationToken = default);
}
