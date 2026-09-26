using Microsoft.AspNetCore.SignalR;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.DTOs.Driver;
using PakiCarpool.Domain.DTOs.Passenger;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.Services.Implementations;

/// <summary>
/// Implements all passenger-facing business operations:
/// ride browsing, booking lifecycle (request / cancel), active booking tracking, and driver no-show reporting.
/// Seat capacity is checked per stop segment via IBookingRepository (Dapper-backed).
/// </summary>
public class PassengerService : IPassengerService
{
    private readonly IUserRepository _userRepository;
    private readonly IRideRepository _rideRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly INotificationService _notificationService;
    private readonly ITrustService _trustService;
    private readonly IEmailService _emailService;
    private readonly IHubContext<Microsoft.AspNetCore.SignalR.Hub> _hubContext;

    public PassengerService(
        IUserRepository userRepository,
        IRideRepository rideRepository,
        IBookingRepository bookingRepository,
        INotificationService notificationService,
        ITrustService trustService,
        IEmailService emailService,
        IHubContext<Microsoft.AspNetCore.SignalR.Hub> hubContext)
    {
        _userRepository = userRepository;
        _rideRepository = rideRepository;
        _bookingRepository = bookingRepository;
        _notificationService = notificationService;
        _trustService = trustService;
        _emailService = emailService;
        _hubContext = hubContext;
    }

    /// <summary>
    /// Lists all available (Posted) rides with stops and driver info, paginated.
    /// </summary>
    public async Task<ApiResponse<PaginatedResult<RideResponseDto>>> GetAvailableRidesAsync(PaginationParams pagination)
    {
        var (items, totalCount) = await _rideRepository.GetAvailableRidesPaginatedAsync(
            pagination.Page, pagination.PageSize);

        var mapped = new List<RideResponseDto>();
        foreach (var ride in items)
        {
            DriverLocation? driverLocation = null;
            if (ride.DriverId != Guid.Empty)
            {
                driverLocation = await _rideRepository.GetDriverLocationAsync(ride.DriverId, ride.Id);
            }
            mapped.Add(MapRideToResponseDto(ride, driverLocation));
        }

        var result = PaginatedResult<RideResponseDto>.Create(
            mapped, totalCount, pagination.Page, pagination.PageSize);

        return ApiResponse<PaginatedResult<RideResponseDto>>.Success(result);
    }

    /// <summary>
    /// Returns detailed information about a specific ride including stops and capacity context.
    /// </summary>
    public async Task<ApiResponse<RideResponseDto>> GetRideDetailAsync(Guid rideId)
    {
        var ride = await _rideRepository.GetByIdAsync(rideId, includeStops: true, includeBookings: true);
        if (ride is null)
            return new ApiResponse<RideResponseDto> { Message = ErrorCodes.NotFound };

        if (ride.Status is not RideStatus.Posted and not RideStatus.Active)
            return new ApiResponse<RideResponseDto> { Message = "This ride is no longer available." };

        DriverLocation? driverLocation = null;
        if (ride.DriverId != Guid.Empty)
        {
            driverLocation = await _rideRepository.GetDriverLocationAsync(ride.DriverId, ride.Id);
        }

        return ApiResponse<RideResponseDto>.Success(MapRideToResponseDto(ride, driverLocation));
    }

    /// <summary>
    /// Creates a booking request. Validates: no active booking, pickup stop not passed,
    /// dropoff after pickup, and per-segment seat capacity. Starts 2-minute timeout window.
    /// </summary>
    public async Task<ApiResponse<object>> CreateBookingAsync(Guid passengerId, CreateBookingDto dto)
    {
        var passenger = await _userRepository.GetByIdAsync(passengerId);
        if (passenger is null || passenger.Role != UserRole.Passenger)
            return new ApiResponse<object> { Message = ErrorCodes.Forbidden };

        if (passenger.IsSuspended || !passenger.IsActive)
            return new ApiResponse<object> { Message = ErrorCodes.AccountSuspended };

        var existingBooking = await _bookingRepository.GetActiveBookingByPassengerIdAsync(passengerId);
        if (existingBooking is not null)
            return new ApiResponse<object> { Message = ErrorCodes.ActiveBookingExists };

        var ride = await _rideRepository.GetByIdAsync(dto.RideId, includeStops: true, includeBookings: true);
        if (ride is null)
            return new ApiResponse<object> { Message = ErrorCodes.NotFound };

        if (ride.Status is not RideStatus.Posted and not RideStatus.Active)
            return new ApiResponse<object> { Message = "This ride is no longer available for booking." };

        var pickupStop = ride.Stops?.FirstOrDefault(s => s.Id == dto.PickupStopId);
        var dropoffStop = ride.Stops?.FirstOrDefault(s => s.Id == dto.DropoffStopId);

        if (pickupStop is null || dropoffStop is null)
            return new ApiResponse<object> { Message = "Invalid pickup or dropoff stop for this ride." };

        if (pickupStop.IsCompleted)
            return new ApiResponse<object> { Message = ErrorCodes.StopAlreadyPassed };

        if (dropoffStop.SequenceOrder <= pickupStop.SequenceOrder)
            return new ApiResponse<object>
            {
                Message = "Dropoff stop must be after the pickup stop in the route sequence."
            };

        var overlapping = await _bookingRepository.GetOverlappingBookingCountAsync(
            ride.Id, pickupStop.SequenceOrder, dropoffStop.SequenceOrder);

        if (overlapping >= ride.MaxSeats)
            return new ApiResponse<object> { Message = ErrorCodes.StopSegmentFull };

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            RideId = ride.Id,
            PassengerId = passengerId,
            PickupStopId = pickupStop.Id,
            DropoffStopId = dropoffStop.Id,
            Status = BookingStatus.Pending,
            RequestedAt = DateTime.UtcNow
        };

        var created = await _bookingRepository.CreateAsync(booking);

        var driverUserId = ride.Driver?.UserId
            ?? (await _userRepository.GetDriverProfileByIdAsync(ride.DriverId))?.UserId;

        if (driverUserId.HasValue)
        {
            await _hubContext.Clients
                .Group($"user-{driverUserId.Value}")
                .SendAsync("BookingRequestReceived", new
                {
                    bookingId = created.Id,
                    passengerName = passenger.FullName,
                    pickupStop = pickupStop.Name,
                    dropoffStop = dropoffStop.Name,
                    requestedAt = created.RequestedAt
                });

            await _notificationService.SendNotificationAsync(
                driverUserId.Value,
                NotificationType.BookingRequest,
                "New Booking Request",
                $"{passenger.FullName} requested a seat from {pickupStop.Name} to {dropoffStop.Name}.");
        }

        return ApiResponse<object>.Success(
            new { bookingId = created.Id, status = created.Status.ToString() },
            "Booking request sent. Waiting for driver acceptance (2 minutes).");
    }

    /// <summary>
    /// Cancels a booking after acceptance. Requires reason (min 10 chars).
    /// Classifies cancellation; frivolous reasons deduct trust points.
    /// </summary>
    public async Task<ApiResponse<object>> CancelBookingAsync(Guid passengerId, Guid bookingId, CancelBookingDto dto)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId, includeRide: true, includeStops: true);
        if (booking is null || booking.PassengerId != passengerId)
            return new ApiResponse<object> { Message = ErrorCodes.NotFound };

        if (booking.Status != BookingStatus.Accepted && booking.Status != BookingStatus.Pending)
            return new ApiResponse<object>
            {
                Message = "Only pending or accepted bookings can be cancelled."
            };

        if (booking.Status == BookingStatus.Accepted)
        {
            var pickupStop = booking.PickupStop
                ?? await _rideRepository.GetStopByIdAsync(booking.PickupStopId);

            if (pickupStop is not null && pickupStop.IsCompleted)
                return new ApiResponse<object>
                {
                    Message = "Cannot cancel after the driver has arrived at your pickup stop."
                };
        }

        if (string.IsNullOrWhiteSpace(dto.Reason) || dto.Reason.Trim().Length < AppConstants.MinCancelReasonLength)
            return new ApiResponse<object>
            {
                Message = $"Cancellation reason must be at least {AppConstants.MinCancelReasonLength} characters."
            };

        var classification = _trustService.ClassifyCancellation(dto.Reason);
        var pointsDeducted = 0;

        booking.Status = BookingStatus.Cancelled;
        booking.CancelReason = dto.Reason.Trim();
        booking.CancelClassification = classification;
        await _bookingRepository.UpdateAsync(booking);

        if (classification == CancellationClassification.Frivolous)
        {
            pointsDeducted = AppConstants.FrivolousCancellationPenalty;
            await _trustService.DeductPointsAsync(passengerId, pointsDeducted);
        }

        await _userRepository.CreateCancellationLogAsync(new CancellationLog
        {
            Id = Guid.NewGuid(),
            UserId = passengerId,
            EntityId = bookingId,
            EntityType = "Booking",
            Reason = dto.Reason.Trim(),
            Classification = classification,
            TrustPointsDeducted = pointsDeducted,
            CreatedAt = DateTime.UtcNow
        });

        return ApiResponse<object>.SuccessMessage(
            classification == CancellationClassification.Frivolous
                ? "Booking cancelled. A trust-score penalty was applied for a frivolous reason."
                : "Booking cancelled successfully.");
    }

    /// <summary>
    /// Returns the passenger's currently active booking with driver, ride, and live location details.
    /// </summary>
    public async Task<ApiResponse<ActiveBookingResponseDto>> GetActiveBookingAsync(Guid passengerId)
    {
        var booking = await _bookingRepository.GetActiveBookingByPassengerIdAsync(passengerId);
        if (booking is null)
            return new ApiResponse<ActiveBookingResponseDto> { Message = "No active booking found." };

        var ride = await _rideRepository.GetByIdAsync(
            booking.RideId, includeStops: true, includeBookings: false);
        if (ride is null)
            return new ApiResponse<ActiveBookingResponseDto> { Message = ErrorCodes.NotFound };

        var pickupStop = ride.Stops?.FirstOrDefault(s => s.Id == booking.PickupStopId);
        var dropoffStop = ride.Stops?.FirstOrDefault(s => s.Id == booking.DropoffStopId);

        var driverProfile = ride.Driver
            ?? await _userRepository.GetDriverProfileByIdAsync(ride.DriverId);
        var driverUser = driverProfile?.User
            ?? (driverProfile is not null
                ? await _userRepository.GetByIdAsync(driverProfile.UserId)
                : null);

        double? driverLat = null;
        double? driverLng = null;
        if (driverProfile is not null)
        {
            var location = await _rideRepository.GetDriverLocationAsync(driverProfile.Id, ride.Id);
            if (location is not null)
            {
                driverLat = location.Latitude;
                driverLng = location.Longitude;
            }
        }

        int? etaSeconds = null;
        if (driverLat.HasValue && driverLng.HasValue && pickupStop != null && !pickupStop.IsCompleted)
        {
            etaSeconds = CalculateEtaSeconds(driverLat.Value, driverLng.Value, pickupStop.Latitude, pickupStop.Longitude);
        }

        var response = new ActiveBookingResponseDto
        {
            Id = booking.Id,
            RideId = ride.Id,
            DriverName = driverUser?.FullName ?? "Unknown",
            CarMake = ride.Car?.Make ?? string.Empty,
            CarModel = ride.Car?.Model ?? string.Empty,
            PickupStopName = pickupStop?.Name ?? "Unknown",
            DropoffStopName = dropoffStop?.Name ?? "Unknown",
            Fare = ride.Fare,
            Status = booking.Status.ToString(),
            RequestedAt = booking.RequestedAt,
            AcceptedAt = booking.AcceptedAt,
            DriverLat = driverLat,
            DriverLng = driverLng,
            EtaSeconds = etaSeconds
        };

        return ApiResponse<ActiveBookingResponseDto>.Success(response);
    }

    /// <summary>
    /// Reports driver no-show after the passenger has waited. Deducts 10 trust points from the driver.
    /// </summary>
    public async Task<ApiResponse<object>> MarkDriverNoShowAsync(Guid passengerId, Guid bookingId)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId, includeRide: true);
        if (booking is null || booking.PassengerId != passengerId)
            return new ApiResponse<object> { Message = ErrorCodes.NotFound };

        if (booking.Status != BookingStatus.Accepted && booking.Status != BookingStatus.Active)
            return new ApiResponse<object>
            {
                Message = "Booking must be Accepted or Active to report a driver no-show."
            };

        var ride = booking.Ride ?? await _rideRepository.GetByIdAsync(booking.RideId);
        if (ride is null)
            return new ApiResponse<object> { Message = ErrorCodes.NotFound };

        var driverProfile = await _userRepository.GetDriverProfileByIdAsync(ride.DriverId);
        if (driverProfile is null)
            return new ApiResponse<object> { Message = "Driver profile not found." };

        booking.Status = BookingStatus.Cancelled;
        booking.CancelReason = "Driver no-show";
        await _bookingRepository.UpdateAsync(booking);

        await _userRepository.CreateNoShowLogAsync(new NoShowLog
        {
            Id = Guid.NewGuid(),
            ReporterId = passengerId,
            OffenderId = driverProfile.UserId,
            RideId = ride.Id,
            CreatedAt = DateTime.UtcNow
        });

        await _trustService.DeductPointsAsync(driverProfile.UserId, AppConstants.NoShowPenalty);

        var driverUser = await _userRepository.GetByIdAsync(driverProfile.UserId);
        if (driverUser is not null)
        {
            await _notificationService.SendInAppNotificationAsync(
                driverProfile.UserId,
                NotificationType.NoShowRecorded,
                "No-Show Recorded",
                "A passenger reported you as a no-show for a recent ride.");
            await _emailService.SendNoShowEmailAsync(driverUser.Email, driverUser.FullName);
        }

        return ApiResponse<object>.SuccessMessage("Driver no-show recorded.");
    }

    private static RideResponseDto MapRideToResponseDto(Ride ride, DriverLocation? driverLocation = null)
    {
        var driverName = ride.Driver?.User?.FullName ?? "Unknown";
        var driverRating = ride.Driver?.AverageRating ?? 0m;

        return new RideResponseDto
        {
            Id = ride.Id,
            DriverId = ride.DriverId,
            DriverName = driverName,
            DriverRating = driverRating,
            CarMake = ride.Car?.Make ?? string.Empty,
            CarModel = ride.Car?.Model ?? string.Empty,
            Fare = ride.Fare,
            MaxSeats = ride.MaxSeats,
            Status = ride.Status.ToString(),
            CreatedAt = ride.CreatedAt,
            Stops = (ride.Stops ?? Enumerable.Empty<RideStop>())
                .OrderBy(s => s.SequenceOrder)
                .Select(s => new RideStopResponseDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    Lat = s.Latitude,
                    Lng = s.Longitude,
                    SequenceOrder = s.SequenceOrder,
                    IsCompleted = s.IsCompleted,
                    ArrivedAt = s.ArrivedAt,
                    EtaSeconds = (!s.IsCompleted && driverLocation != null)
                        ? CalculateEtaSeconds(driverLocation.Latitude, driverLocation.Longitude, s.Latitude, s.Longitude)
                        : null
                })
                .ToList()
        };
    }

    private static int CalculateEtaSeconds(double lat1, double lon1, double lat2, double lon2)
    {
        var r = 6371.0; // Earth radius in km
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var rLat1 = lat1 * Math.PI / 180.0;
        var rLat2 = lat2 * Math.PI / 180.0;

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(rLat1) * Math.Cos(rLat2) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        var distanceKm = r * c;

        var speedKmH = 30.0;
        var hours = distanceKm / speedKmH;
        return (int)Math.Round(hours * 3600);
    }
}
