using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.DTOs.Driver;

namespace PakiCarpool.Interfaces.IServices;

/// <summary>
/// Defines all driver-facing business operations.
/// Covers car management, ride lifecycle, booking decisions, stop progression, and location tracking.
/// </summary>
public interface IDriverService
{
    /// <summary>
    /// Registers a new car for the driver. Requires admin approval before use.
    /// </summary>
    Task<ApiResponse<CarStatusResponseDto>> RegisterCarAsync(Guid driverId, RegisterCarDto dto);

    /// <summary>
    /// Updates an existing car's details. Resets status to Pending for re-approval.
    /// </summary>
    Task<ApiResponse<CarStatusResponseDto>> UpdateCarAsync(Guid driverId, Guid carId, UpdateCarDto dto);

    /// <summary>
    /// Returns the current approval status of the driver's car(s).
    /// </summary>
    Task<ApiResponse<List<CarStatusResponseDto>>> GetCarStatusAsync(Guid driverId);

    /// <summary>
    /// Posts a new ride with stops and fare. Validates no active ride, approved car, and liveness.
    /// Sets CancelGraceExpiresAt = CreatedAt + 5 minutes.
    /// </summary>
    Task<ApiResponse<RideResponseDto>> PostRideAsync(Guid driverId, PostRideDto dto);

    /// <summary>
    /// Cancels a ride. Within grace period = no penalty. After = cancellation classification applies.
    /// Notifies all accepted passengers.
    /// </summary>
    Task<ApiResponse<object>> CancelRideAsync(Guid driverId, Guid rideId, CancelRideDto dto);

    /// <summary>
    /// Returns the driver's currently active ride with stops and bookings.
    /// </summary>
    Task<ApiResponse<RideResponseDto>> GetActiveRideAsync(Guid driverId);

    /// <summary>
    /// Lists all bookings for a specific ride owned by this driver.
    /// </summary>
    Task<ApiResponse<PaginatedResult<BookingDetailDto>>> GetRideBookingsAsync(
        Guid driverId,
        Guid rideId,
        PaginationParams pagination);

    /// <summary>
    /// Accepts a pending booking request. Re-validates seat capacity for race condition safety.
    /// </summary>
    Task<ApiResponse<object>> AcceptBookingAsync(Guid driverId, Guid bookingId);

    /// <summary>
    /// Rejects a pending booking request.
    /// </summary>
    Task<ApiResponse<object>> RejectBookingAsync(Guid driverId, Guid bookingId);

    /// <summary>
    /// Marks a passenger as no-show after 3-minute wait at pickup stop.
    /// Deducts 10 trust points from passenger.
    /// </summary>
    Task<ApiResponse<object>> MarkPassengerNoShowAsync(Guid driverId, Guid bookingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the ride for a specific passenger. Sets booking to Completed.
    /// </summary>
    Task<ApiResponse<object>> EndRideForPassengerAsync(Guid driverId, Guid bookingId);

    /// <summary>
    /// Confirms cash payment received from passenger.
    /// </summary>
    Task<ApiResponse<object>> ConfirmCashReceivedAsync(Guid driverId, Guid bookingId);

    /// <summary>
    /// Marks the next stop as active when the driver begins travelling toward it.
    /// </summary>
    Task<ApiResponse<object>> BeginStopTravelAsync(Guid driverId, Guid stopId);

    /// <summary>
    /// Marks a stop as arrived. Notifies passengers at this pickup. Generates next navigation link.
    /// </summary>
    Task<ApiResponse<object>> MarkStopArrivedAsync(Guid driverId, Guid stopId);

    /// <summary>
    /// Updates driver's GPS location. Upsert pattern: one record per driver per ride.
    /// </summary>
    Task<ApiResponse<object>> UpdateLocationAsync(Guid driverId, DriverLocationDto dto);

    /// <summary>
    /// Notifies a specific passenger that the driver has arrived at their pickup location.
    /// </summary>
    Task<ApiResponse<object>> NotifyIAmHereAsync(Guid driverId, Guid bookingId);
}
