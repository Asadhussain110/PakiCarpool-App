using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.DTOs.Driver;
using PakiCarpool.Domain.DTOs.Passenger;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Repository.EFCore;

namespace PakiCarpool.Services.Implementations;

/// <summary>
/// Implements all passenger-facing business operations:
/// ride browsing, booking lifecycle (request / cancel), active booking tracking, and driver no-show reporting.
/// Seat capacity is checked per stop segment via IBookingRepository (Dapper-backed).
/// </summary>
public class PassengerService : IPassengerService
{
    private readonly IUserRepository _userRepository;
    private readonly PakiCarpoolDbContext _context;
    private readonly IRideRepository _rideRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly INotificationService _notificationService;
    private readonly IFlagService _flagService;
    private readonly ITrustService _trustService;
    private readonly IEmailService _emailService;
    private readonly IHubContext<Microsoft.AspNetCore.SignalR.Hub> _hubContext;
    private readonly TimeProvider _timeProvider;

    public PassengerService(
        IUserRepository userRepository,
        PakiCarpoolDbContext context,
        IRideRepository rideRepository,
        IBookingRepository bookingRepository,
        INotificationService notificationService,
        IFlagService flagService,
        ITrustService trustService,
        IEmailService emailService,
        IHubContext<Microsoft.AspNetCore.SignalR.Hub> hubContext,
        TimeProvider? timeProvider = null)
    {
        _userRepository = userRepository;
        _context = context;
        _rideRepository = rideRepository;
        _bookingRepository = bookingRepository;
        _notificationService = notificationService;
        _flagService = flagService;
        _trustService = trustService;
        _emailService = emailService;
        _hubContext = hubContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Lists all available (Posted) rides with stops and driver info, paginated.
    /// </summary>
    public async Task<ApiResponse<PaginatedResult<RideResponseDto>>> GetAvailableRidesAsync(PaginationParams pagination)
    {
        var (items, totalCount) = await _rideRepository.GetAvailableRidesPaginatedAsync(
            pagination.Page, pagination.PageSize);

        var rideIds = items.Select(ride => ride.Id).ToArray();
        var locationsByDriverAndRide = (await _context.DriverLocations
                .AsNoTracking()
                .Where(location => rideIds.Contains(location.RideId))
                .ToListAsync())
            .ToDictionary(location => (location.DriverId, location.RideId));

        var mapped = new List<RideResponseDto>(items.Count);
        foreach (var ride in items)
        {
            locationsByDriverAndRide.TryGetValue((ride.DriverId, ride.Id), out var driverLocation);
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

        Ride ride;
        RideStop pickupStop;
        RideStop dropoffStop;
        Booking created;
        await using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            async Task<ApiResponse<object>> RollbackWithErrorAsync(string message)
            {
                await transaction.RollbackAsync();
                return new ApiResponse<object> { Message = message };
            }

            try
            {
                var existingBooking = await _bookingRepository.GetActiveBookingByPassengerIdAsync(passengerId);
                if (existingBooking is not null)
                    return await RollbackWithErrorAsync(ErrorCodes.ActiveBookingExists);

                var loadedRide = await _rideRepository.GetByIdAsync(dto.RideId, includeStops: true, includeBookings: true);
                if (loadedRide is null)
                    return await RollbackWithErrorAsync(ErrorCodes.NotFound);
                ride = loadedRide;

                if (ride.Status is not RideStatus.Posted and not RideStatus.Active)
                    return await RollbackWithErrorAsync("This ride is no longer available for booking.");

                pickupStop = ride.Stops?.FirstOrDefault(s => s.Id == dto.PickupStopId)!;
                dropoffStop = ride.Stops?.FirstOrDefault(s => s.Id == dto.DropoffStopId)!;

                if (pickupStop is null || dropoffStop is null)
                    return await RollbackWithErrorAsync("Invalid pickup or dropoff stop for this ride.");

                if (pickupStop.Status != RideStopStatus.Upcoming)
                    return await RollbackWithErrorAsync(ErrorCodes.StopAlreadyPassed);

                if (dropoffStop.Status != RideStopStatus.Upcoming)
                    return await RollbackWithErrorAsync("Invalid pickup or dropoff stop for this ride.");

                if (dropoffStop.SequenceOrder <= pickupStop.SequenceOrder)
                    return await RollbackWithErrorAsync("Dropoff stop must be after the pickup stop in the route sequence.");

                var overlapping = await (
                      from capacityBooking in _context.Bookings
                      join bookingPickup in _context.RideStops on capacityBooking.PickupStopId equals bookingPickup.Id
                      join bookingDropoff in _context.RideStops on capacityBooking.DropoffStopId equals bookingDropoff.Id
                      where capacityBooking.RideId == ride.Id &&
                          (capacityBooking.Status == BookingStatus.Accepted || capacityBooking.Status == BookingStatus.Active) &&
                          bookingPickup.SequenceOrder < dropoffStop.SequenceOrder &&
                          bookingDropoff.SequenceOrder > pickupStop.SequenceOrder
                      select capacityBooking.Id)
                    .CountAsync();

                if (overlapping >= ride.MaxSeats)
                    return await RollbackWithErrorAsync(ErrorCodes.StopSegmentFull);

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

                created = await _bookingRepository.CreateAsync(booking);
                await transaction.CommitAsync();
            }
            catch (DbUpdateException exception) when (
                exception.InnerException?.Message.Contains(
                    "ix_bookings_active_passenger", StringComparison.OrdinalIgnoreCase) == true)
            {
                await transaction.RollbackAsync();
                return new ApiResponse<object> { Message = "You already have an active booking." };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

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

            if (pickupStop is not null && pickupStop.Status == RideStopStatus.Completed)
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
        await _notificationService.RemovePassengerFromRideGroupsAsync(booking.RideId, booking.PassengerId);

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
        if (driverLat.HasValue && driverLng.HasValue && pickupStop != null &&
            pickupStop.Status != RideStopStatus.Completed)
        {
            etaSeconds = CalculateEtaSeconds(driverLat.Value, driverLng.Value, pickupStop.Latitude, pickupStop.Longitude);
        }

        var driverPhotoUrl = driverProfile?.ProfilePhotoPath;
        if (!string.IsNullOrEmpty(driverPhotoUrl) && driverProfile?.PhotoUpdatedAt.HasValue == true)
        {
            driverPhotoUrl = $"{driverPhotoUrl}?v={driverProfile.PhotoUpdatedAt.Value.Ticks}";
        }

        var response = new ActiveBookingResponseDto
        {
            Id = booking.Id,
            RideId = ride.Id,
            DriverName = driverUser?.FullName ?? "Unknown",
            DriverPhotoUrl = driverPhotoUrl,
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
    /// Records a driver no-show. Only the booking's passenger may report it, and only when
    /// all of these hold: the booking is Accepted, the ride is not Cancelled or Completed,
    /// at least 3 minutes have passed since the booking was accepted, the driver has not
    /// arrived at the passenger's pickup stop, and the driver is not visibly en route
    /// (no fresh GPS update within the last 3 minutes) unless the pickup was skipped.
    /// The log, the -10 trust penalty for the driver, and the booking cancellation are
    /// persisted atomically; one report per booking is enforced by the database, so
    /// repeated or concurrent reports never double-penalize. The passenger receives no
    /// penalty or cancellation log — the driver is the offender.
    /// </summary>
    public async Task<ApiResponse<object>> MarkDriverNoShowAsync(
        Guid passengerId,
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        var booking = await _bookingRepository.GetByIdAsync(
            bookingId,
            includeRide: true,
            cancellationToken: cancellationToken);
        if (booking is null || booking.PassengerId != passengerId)
            return new ApiResponse<object> { Message = ErrorCodes.NotFound };

        if (booking.Status != BookingStatus.Accepted || booking.AcceptedAt is not DateTime acceptedAt)
            return new ApiResponse<object>
            {
                Message = "Booking must be Accepted to report a driver no-show."
            };

        var ride = booking.Ride ?? await _rideRepository.GetByIdAsync(booking.RideId, cancellationToken: cancellationToken);
        if (ride is null)
            return new ApiResponse<object> { Message = ErrorCodes.NotFound };

        if (ride.Status is RideStatus.Cancelled or RideStatus.Completed)
            return new ApiResponse<object> { Message = "Ride is no longer active." };

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (now - acceptedAt < TimeSpan.FromMinutes(AppConstants.NoShowWaitMinutes))
            return new ApiResponse<object>
            {
                Message = $"The {AppConstants.NoShowWaitMinutes}-minute wait since your booking was accepted has not elapsed."
            };

        var stops = ride.Stops?.ToList()
            ?? (await _rideRepository.GetByIdAsync(ride.Id, includeStops: true, cancellationToken: cancellationToken))?.Stops?.ToList()
            ?? [];
        var pickupStop = stops.FirstOrDefault(s => s.Id == booking.PickupStopId)
            ?? await _rideRepository.GetStopByIdAsync(booking.PickupStopId, cancellationToken);
        if (pickupStop is null)
            return new ApiResponse<object> { Message = ErrorCodes.NotFound };

        if (pickupStop.Status == RideStopStatus.Completed || pickupStop.ArrivedAt.HasValue)
            return new ApiResponse<object>
            {
                Message = "The driver has arrived at your pickup stop."
            };

        // En-route protection: a driver with fresh GPS who has not skipped this pickup
        // is still on the way (whether the ride is Posted or Active) — not a no-show.
        var skippedPickup = stops.Any(s =>
            s.SequenceOrder > pickupStop.SequenceOrder &&
            s.Status == RideStopStatus.Completed);
        var driverLocation = await _rideRepository.GetDriverLocationAsync(ride.DriverId, ride.Id);
        var gpsIsFresh = driverLocation is not null &&
            driverLocation.RecordedAt >= now.AddMinutes(-AppConstants.NoShowGpsFreshnessMinutes);
        if (gpsIsFresh && !skippedPickup)
            return new ApiResponse<object>
            {
                Message = "The driver appears to be en route to your pickup stop."
            };

        var driverProfile = ride.Driver
            ?? await _userRepository.GetDriverProfileByIdAsync(ride.DriverId, cancellationToken);
        if (driverProfile is null)
            return new ApiResponse<object> { Message = "Driver profile not found." };

        var log = new NoShowLog
        {
            Id = Guid.NewGuid(),
            ReporterId = passengerId,
            OffenderId = driverProfile.UserId,
            RideId = ride.Id,
            BookingId = booking.Id,
            CreatedAt = now
        };

        var outcome = await _bookingRepository.RecordNoShowAsync(
            log,
            [BookingStatus.Accepted],
            AppConstants.NoShowDriverCancelReason,
            AppConstants.NoShowPenalty,
            now,
            cancellationToken);

        if (outcome != NoShowRecordingOutcome.Recorded)
        {
            var message = outcome switch
            {
                NoShowRecordingOutcome.AlreadyReported =>
                    "A no-show has already been recorded for this booking.",
                NoShowRecordingOutcome.BookingNotEligible =>
                    "Booking is no longer eligible for a driver no-show report.",
                _ => ErrorCodes.NotFound
            };
            return new ApiResponse<object> { Message = message };
        }

        // Post-commit side effects: free the seat by the same status-based occupancy
        // and group-removal semantics as every other booking cancellation — no passenger
        // penalty and no passenger cancellation log, because the driver is the offender.
        // Only this booking is cancelled; the ride and other passengers are untouched.
        await _notificationService.RemovePassengerFromRideGroupsAsync(booking.RideId, booking.PassengerId);

        await _notificationService.SendInAppNotificationAsync(
            passengerId,
            NotificationType.BookingAutoCancelled,
            "Booking Cancelled",
            "Your booking was cancelled because the driver did not show at your pickup stop.");

        await _notificationService.SendInAppNotificationAsync(
            driverProfile.UserId,
            NotificationType.NoShowRecorded,
            "No-Show Recorded",
            "You were recorded as a no-show at a passenger's pickup stop. Repeated no-shows lead to admin review of your account.");
        var driverUser = await _userRepository.GetByIdAsync(driverProfile.UserId, cancellationToken);
        if (driverUser is not null)
            await _emailService.SendNoShowEmailAsync(driverUser.Email, driverUser.FullName);

        await _trustService.CheckThresholdsAsync(driverProfile.UserId);

        return ApiResponse<object>.SuccessMessage(
            "Driver no-show recorded. Your booking has been cancelled.");
    }

    private static RideResponseDto MapRideToResponseDto(Ride ride, DriverLocation? driverLocation = null)
    {
        var driverName = ride.Driver?.User?.FullName ?? "Unknown";
        var driverRating = ride.Driver?.AverageRating ?? 0m;

        var driverPhotoUrl = ride.Driver?.ProfilePhotoPath;
        if (!string.IsNullOrEmpty(driverPhotoUrl) && ride.Driver?.PhotoUpdatedAt.HasValue == true)
        {
            driverPhotoUrl = $"{driverPhotoUrl}?v={ride.Driver.PhotoUpdatedAt.Value.Ticks}";
        }

        return new RideResponseDto
        {
            Id = ride.Id,
            DriverId = ride.DriverId,
            DriverUserId = ride.Driver?.UserId ?? Guid.Empty,
            DriverName = driverName,
            DriverRating = driverRating,
            DriverPhotoUrl = driverPhotoUrl,
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
                    Status = s.Status.ToString(),
                    ArrivedAt = s.ArrivedAt,
                    EtaSeconds = (s.Status != RideStopStatus.Completed && driverLocation != null)
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
