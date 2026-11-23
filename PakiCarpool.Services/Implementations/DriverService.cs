using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.DTOs.Driver;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Repository.EFCore;
using PakiCarpool.Security.Helpers;

namespace PakiCarpool.Services.Implementations;

/// <summary>
/// Implements all driver-facing business operations:
/// car management, ride lifecycle, booking decisions, stop progression, and location tracking.
/// No DbContext calls — all persistence via IUserRepository, IRideRepository, IBookingRepository.
/// </summary>
public class DriverService : IDriverService
{
    private readonly IUserRepository _userRepository;
    private readonly PakiCarpoolDbContext _context;
    private readonly IRideRepository _rideRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly INotificationService _notificationService;
    private readonly IFlagService _flagService;
    private readonly ITrustService _trustService;
    private readonly IEmailService _emailService;
    private readonly AesEncryptionHelper _aesHelper;
    private readonly IHubContext<Microsoft.AspNetCore.SignalR.Hub> _hubContext;

    public DriverService(
        IUserRepository userRepository,
        PakiCarpoolDbContext context,
        IRideRepository rideRepository,
        IBookingRepository bookingRepository,
        INotificationService notificationService,
        IFlagService flagService,
        ITrustService trustService,
        IEmailService emailService,
        AesEncryptionHelper aesHelper,
        IHubContext<Microsoft.AspNetCore.SignalR.Hub> hubContext)
    {
        _userRepository = userRepository;
        _context = context;
        _rideRepository = rideRepository;
        _bookingRepository = bookingRepository;
        _notificationService = notificationService;
        _flagService = flagService;
        _trustService = trustService;
        _emailService = emailService;
        _aesHelper = aesHelper;
        _hubContext = hubContext;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CAR MANAGEMENT
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a new car for the driver. Encrypts license plate with AES-256.
    /// Car status defaults to Pending — requires admin approval.
    /// </summary>
    public async Task<ApiResponse<CarStatusResponseDto>> RegisterCarAsync(Guid driverId, RegisterCarDto dto)
    {
        var profile = await _userRepository.GetDriverProfileByUserIdAsync(driverId);
        if (profile is null)
            return new ApiResponse<CarStatusResponseDto> { Message = "Driver profile not found." };

        var encryptedPlate = _aesHelper.Encrypt(dto.LicensePlate.Trim().ToUpperInvariant());

        var car = new DriverCar
        {
            Id = Guid.NewGuid(),
            DriverId = profile.Id,
            Make = dto.Make.Trim(),
            Model = dto.Model.Trim(),
            Year = dto.Year,
            LicensePlate = encryptedPlate,
            AvailableSeats = dto.AvailableSeats,
            Status = CarStatus.Pending,
            PhotoPaths = System.Text.Json.JsonSerializer.Serialize(dto.PhotoPaths),
            RegDocPath = dto.RegDocPath,
            CreatedAt = DateTime.UtcNow
        };

        var created = await _userRepository.CreateDriverCarAsync(car);

        // Notify admins of new car registration (admin portal in-app)
        var (admins, _) = await _userRepository.GetAllPaginatedAsync(1, 50);
        var adminIds = admins.Where(u => u.Role == UserRole.Admin).Select(u => u.Id);
        await _notificationService.SendInAppNotificationToGroupAsync(
            adminIds,
            NotificationType.NewDriverRegistration,
            "New Car Registration",
            $"{profile.User?.FullName ?? "A driver"} submitted a {created.Make} {created.Model} for approval.");

        return ApiResponse<CarStatusResponseDto>.Success(
            MapCarToStatusDto(created),
            "Car registered. Awaiting admin approval.");
    }

    /// <summary>
    /// Updates an existing car's details. Resets status to Pending for re-approval.
    /// </summary>
    public async Task<ApiResponse<CarStatusResponseDto>> UpdateCarAsync(Guid driverId, Guid carId, UpdateCarDto dto)
    {
        var profile = await _userRepository.GetDriverProfileByUserIdAsync(driverId);
        if (profile is null)
            return new ApiResponse<CarStatusResponseDto> { Message = "Driver profile not found." };

        var car = await _userRepository.GetDriverCarByIdAsync(carId);
        if (car is null || car.DriverId != profile.Id)
            return new ApiResponse<CarStatusResponseDto> { Message = "Car not found." };

        if (dto.Make is not null) car.Make = dto.Make.Trim();
        if (dto.Model is not null) car.Model = dto.Model.Trim();
        if (dto.Year.HasValue) car.Year = dto.Year.Value;
        if (dto.LicensePlate is not null)
            car.LicensePlate = _aesHelper.Encrypt(dto.LicensePlate.Trim().ToUpperInvariant());
        if (dto.AvailableSeats.HasValue) car.AvailableSeats = dto.AvailableSeats.Value;
        if (dto.PhotoPaths is not null)
            car.PhotoPaths = System.Text.Json.JsonSerializer.Serialize(dto.PhotoPaths);
        if (dto.RegDocPath is not null) car.RegDocPath = dto.RegDocPath;

        // Reset to Pending for re-approval
        car.Status = CarStatus.Pending;
        car.RejectionReason = null;
        car.ApprovedAt = null;

        await _userRepository.UpdateDriverCarAsync(car);

        return ApiResponse<CarStatusResponseDto>.Success(
            MapCarToStatusDto(car),
            "Car updated. Awaiting admin re-approval.");
    }

    /// <summary>
    /// Returns the current approval status of all cars registered by the driver.
    /// </summary>
    public async Task<ApiResponse<List<CarStatusResponseDto>>> GetCarStatusAsync(Guid driverId)
    {
        var profile = await _userRepository.GetDriverProfileByUserIdAsync(driverId);
        if (profile is null)
            return new ApiResponse<List<CarStatusResponseDto>> { Message = "Driver profile not found." };

        var cars = await _userRepository.GetDriverCarsAsync(profile.Id);
        var result = cars.Select(MapCarToStatusDto).ToList();

        return ApiResponse<List<CarStatusResponseDto>>.Success(result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // RIDE LIFECYCLE
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Posts a new ride with stops and fare.
    /// Validates: no active ride, approved car, liveness completed, ≥2 stops, unique/sequential orders.
    /// CancelGraceExpiresAt = CreatedAt + 5 minutes.
    /// </summary>
    public async Task<ApiResponse<RideResponseDto>> PostRideAsync(Guid driverId, PostRideDto dto)
    {
        var profile = await _userRepository.GetDriverProfileByUserIdAsync(driverId);
        if (profile is null)
            return new ApiResponse<RideResponseDto> { Message = "Driver profile not found." };

        // Liveness required
        if (!profile.LivenessVerified)
            return new ApiResponse<RideResponseDto> { Message = ErrorCodes.LivenessRequired };

        Ride created;
        await using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            async Task<ApiResponse<RideResponseDto>> RollbackWithErrorAsync(string message)
            {
                await transaction.RollbackAsync();
                return new ApiResponse<RideResponseDto> { Message = message };
            }

            try
            {
                var existingRide = await _rideRepository.GetActiveRideByDriverIdAsync(profile.Id);
                if (existingRide is not null)
                    return await RollbackWithErrorAsync(ErrorCodes.ActiveRideExists);

                // Must have at least one approved car
                var cars = await _userRepository.GetDriverCarsAsync(profile.Id);
                var approvedCar = cars.FirstOrDefault(c => c.Status == CarStatus.Approved);
                if (approvedCar is null)
                    return await RollbackWithErrorAsync(ErrorCodes.CarNotApproved);

                // Validate stop count
                if (dto.Stops.Count < AppConstants.MinStopsPerRide)
                    return await RollbackWithErrorAsync($"A ride must have at least {AppConstants.MinStopsPerRide} stops.");

                // Validate MaxSeats ≤ car capacity
                if (dto.MaxSeats > approvedCar.AvailableSeats)
                    return await RollbackWithErrorAsync($"MaxSeats cannot exceed car capacity of {approvedCar.AvailableSeats}.");

                // Validate sequential and unique stop orders
                var orders = dto.Stops.Select(s => s.SequenceOrder).ToList();
                if (orders.Distinct().Count() != orders.Count || orders.Any(o => o < 1))
                    return await RollbackWithErrorAsync("Stop sequence orders must be unique and start from 1.");

                var now = DateTime.UtcNow;
                var ride = new Ride
                {
                    Id = Guid.NewGuid(),
                    DriverId = profile.Id,
                    CarId = approvedCar.Id,
                    Fare = dto.Fare,
                    MaxSeats = dto.MaxSeats,
                    Status = RideStatus.Posted,
                    CreatedAt = now,
                    CancelGraceExpiresAt = now.AddMinutes(AppConstants.CancelGracePeriodMinutes),
                    Stops = dto.Stops.Select(s => new RideStop
                    {
                        Id = Guid.NewGuid(),
                        Name = s.Name.Trim(),
                        Latitude = s.Lat,
                        Longitude = s.Lng,
                        SequenceOrder = s.SequenceOrder
                    }).ToList()
                };

                created = await _rideRepository.CreateAsync(ride);
                await transaction.CommitAsync();
            }
            catch (DbUpdateException exception) when (
                exception.InnerException?.Message.Contains(
                    "ix_rides_active_driver", StringComparison.OrdinalIgnoreCase) == true)
            {
                await transaction.RollbackAsync();
                return new ApiResponse<RideResponseDto> { Message = "You already have an active ride." };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        return ApiResponse<RideResponseDto>.Success(
            await MapRideToResponseDtoAsync(created, profile),
            "Ride posted successfully.");
    }

    /// <summary>
    /// Cancels a ride. Within 5-minute grace period: no penalty.
    /// After grace period: cancellation classification and trust point deduction apply.
    /// Notifies all Accepted and Active passengers.
    /// </summary>
    public async Task<ApiResponse<object>> CancelRideAsync(Guid driverId, Guid rideId, CancelRideDto dto)
    {
        var profile = await _userRepository.GetDriverProfileByUserIdAsync(driverId);
        if (profile is null)
            return new ApiResponse<object> { Message = "Driver profile not found." };

        var ride = await _rideRepository.GetByIdAsync(rideId, includeBookings: true);
        if (ride is null || ride.DriverId != profile.Id)
            return new ApiResponse<object> { Message = "Ride not found." };

        if (ride.Status is RideStatus.Completed or RideStatus.Cancelled)
            return new ApiResponse<object> { Message = "Ride is already completed or cancelled." };

        var now = DateTime.UtcNow;
        var withinGrace = now < ride.CancelGraceExpiresAt;

        ride.Status = RideStatus.Cancelled;
        ride.CancelReason = dto.Reason;
        await _rideRepository.UpdateAsync(ride);

        // Penalty logic (after grace period)
        if (!withinGrace)
        {
            var classification = _trustService.ClassifyCancellation(dto.Reason);
            if (classification == CancellationClassification.Frivolous)
            {
                await _trustService.DeductPointsAsync(driverId, AppConstants.FrivolousCancellationPenalty);
                var log = new Domain.Models.CancellationLog
                {
                    Id = Guid.NewGuid(),
                    UserId = driverId,
                    EntityId = rideId,
                    EntityType = "Ride",
                    Reason = dto.Reason,
                    Classification = classification,
                    TrustPointsDeducted = AppConstants.FrivolousCancellationPenalty,
                    CreatedAt = now
                };
                await _userRepository.CreateCancellationLogAsync(log);
            }
        }

        // Notify all Accepted and Active passengers
        var bookingsToNotify = ride.Bookings?
            .Where(b => b.Status is BookingStatus.Accepted or BookingStatus.Active)
            .ToList() ?? new List<Booking>();

        // Named event to ride group first (one broadcast vs. N per-user broadcasts)
        await _notificationService.SendRideCancelledByDriverAsync(rideId, dto.Reason);

        foreach (var booking in bookingsToNotify)
        {
            booking.Status = BookingStatus.Cancelled;
            booking.CancelReason = "Ride cancelled by driver";
            _context.Entry(booking).State = EntityState.Modified;
            await _notificationService.RemovePassengerFromRideGroupsAsync(rideId, booking.PassengerId);

            // Keep generic in-app + push so the notification matrix still fires
            await _notificationService.SendInAppNotificationAsync(
                booking.PassengerId,
                NotificationType.RideCancelledByDriver,
                "Ride Cancelled",
                "Your upcoming ride has been cancelled by the driver.");

            var passenger = booking.Passenger;
            if (passenger is not null)
            {
                await _emailService.SendRideCancelledEmailAsync(
                    passenger.Email, passenger.FullName,
                    $"From {ride.Stops?.OrderBy(s => s.SequenceOrder).First().Name} — {ride.CreatedAt:f}");
            }
        }
        await _context.SaveChangesAsync();

        return ApiResponse<object>.SuccessMessage(
            withinGrace ? "Ride cancelled (within grace period)." : "Ride cancelled.");
    }

    /// <summary>
    /// Returns the driver's currently active (Posted or Active) ride.
    /// </summary>
    public async Task<ApiResponse<RideResponseDto>> GetActiveRideAsync(Guid driverId)
    {
        var profile = await _userRepository.GetDriverProfileByUserIdAsync(driverId);
        if (profile is null)
            return new ApiResponse<RideResponseDto> { Message = "Driver profile not found." };

        var ride = await _rideRepository.GetActiveRideByDriverIdAsync(profile.Id);
        if (ride is null)
            return new ApiResponse<RideResponseDto> { Message = "No active ride found." };

        return ApiResponse<RideResponseDto>.Success(
            await MapRideToResponseDtoAsync(ride, profile));
    }

    /// <summary>
    /// Lists all bookings for a specific ride owned by this driver.
    /// </summary>
    public async Task<ApiResponse<PaginatedResult<BookingDetailDto>>> GetRideBookingsAsync(
        Guid driverId,
        Guid rideId,
        PaginationParams pagination)
    {
        var profile = await _userRepository.GetDriverProfileByUserIdAsync(driverId);
        if (profile is null)
            return new ApiResponse<PaginatedResult<BookingDetailDto>> { Message = "Driver profile not found." };

        var ride = await _rideRepository.GetByIdAsync(rideId);
        if (ride is null || ride.DriverId != profile.Id)
            return new ApiResponse<PaginatedResult<BookingDetailDto>> { Message = "Ride not found." };

        var (bookings, totalCount) = await _bookingRepository.GetBookingsByRideIdAsync(
            rideId,
            pagination.Page,
            pagination.PageSize);
        var result = new List<BookingDetailDto>();

        foreach (var booking in bookings)
        {
            result.Add(new BookingDetailDto
            {
                Id = booking.Id,
                PassengerId = booking.PassengerId,
                PassengerName = booking.Passenger?.FullName ?? "Unknown",
                PickupStopName = booking.PickupStop?.Name ?? "Unknown",
                DropoffStopName = booking.DropoffStop?.Name ?? "Unknown",
                Status = booking.Status.ToString(),
                RequestedAt = booking.RequestedAt,
                AcceptedAt = booking.AcceptedAt,
                IsCashPaid = booking.IsCashPaid
            });
        }

        return ApiResponse<PaginatedResult<BookingDetailDto>>.Success(
            PaginatedResult<BookingDetailDto>.Create(
                result,
                totalCount,
                pagination.Page,
                pagination.PageSize));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // BOOKING DECISIONS
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Accepts a pending booking request. Re-validates seat capacity for race condition safety.
    /// Notifies the passenger via SignalR.
    /// </summary>
    public async Task<ApiResponse<object>> AcceptBookingAsync(Guid driverId, Guid bookingId)
    {
        var (profile, booking, error) = await GetBookingForDriverAsync(driverId, bookingId);
        if (error is not null) return new ApiResponse<object> { Message = error };

        if (booking!.Status != BookingStatus.Pending)
        {
            var message = booking.Status switch
            {
                BookingStatus.Cancelled => "Booking was cancelled and can no longer be accepted.",
                BookingStatus.Accepted => "Booking has already been accepted.",
                BookingStatus.Rejected => "Booking has already been rejected.",
                _ => "Booking is no longer in a pending state."
            };
            return new ApiResponse<object> { Message = message };
        }

        // Re-validate seat capacity (race condition guard)
        var pickupStop = await _rideRepository.GetStopByIdAsync(booking.PickupStopId);
        var dropoffStop = await _rideRepository.GetStopByIdAsync(booking.DropoffStopId);
        if (pickupStop is null || dropoffStop is null)
            return new ApiResponse<object> { Message = "Booking stops not found." };

        var ride = await _rideRepository.GetByIdAsync(booking.RideId);
        if (ride is null)
            return new ApiResponse<object> { Message = "Ride not found." };

        var overlapping = await _bookingRepository.GetOverlappingBookingCountAsync(
            booking.RideId,
            pickupStop.SequenceOrder,
            dropoffStop.SequenceOrder);

        if (overlapping >= ride.MaxSeats)
            return new ApiResponse<object> { Message = ErrorCodes.StopSegmentFull };

        booking.Status = BookingStatus.Accepted;
        booking.AcceptedAt = DateTime.UtcNow;
        await _bookingRepository.UpdateAsync(booking);

        // Named event — passenger receives structured payload for Flutter UI
        var driverUser = await _userRepository.GetByIdAsync(driverId);
        var carModel = ride.Car?.Model ?? string.Empty;
        await _notificationService.SendBookingAcceptedAsync(
            booking.PassengerId,
            booking.Id,
            driverUser?.FullName ?? "Your driver",
            carModel);

        // Keep generic in-app + push so the notification matrix still fires
        await _notificationService.SendInAppNotificationAsync(
            booking.PassengerId,
            NotificationType.BookingAccepted,
            "Booking Accepted",
            "Your ride booking has been accepted by the driver.");

        // Auto-reject competing pending bookings that now exceed capacity on the same segment,
        // and notify each affected passenger with the named RideFullNotification event.
        var rideBookings = await _bookingRepository.GetBookingsByRideIdAsync(booking.RideId);
        var pendingCompetitors = rideBookings
            .Where(b => b.Status == BookingStatus.Pending && b.Id != booking.Id)
            .ToList();
        var occupiedSegments = rideBookings
            .Where(b => b.Status is BookingStatus.Accepted or BookingStatus.Active)
            .Select(b => new
            {
                PickupSequenceOrder = b.PickupStop.SequenceOrder,
                DropoffSequenceOrder = b.DropoffStop.SequenceOrder
            })
            .ToList();

        foreach (var competitor in pendingCompetitors)
        {
            var compPickup = competitor.PickupStop;
            var compDropoff = competitor.DropoffStop;
            if (compPickup is null || compDropoff is null) continue;

            var compOverlap = occupiedSegments.Count(segment =>
                segment.PickupSequenceOrder < compDropoff.SequenceOrder &&
                segment.DropoffSequenceOrder > compPickup.SequenceOrder);

            if (compOverlap >= ride.MaxSeats)
            {
                competitor.Status = BookingStatus.Rejected;
                competitor.CancelReason = "Auto-rejected: ride segment is now full";
                _context.Entry(competitor).State = EntityState.Modified;
                await _notificationService.RemovePassengerFromRideGroupsAsync(competitor.RideId, competitor.PassengerId);

                // Named RideFullNotification event to the displaced passenger
                await _notificationService.SendRideFullNotificationAsync(
                    competitor.PassengerId, booking.RideId);
            }
        }
        await _context.SaveChangesAsync();

        return ApiResponse<object>.SuccessMessage("Booking accepted.");
    }

    /// <summary>
    /// Rejects a pending booking request.
    /// </summary>
    public async Task<ApiResponse<object>> RejectBookingAsync(Guid driverId, Guid bookingId)
    {
        var (_, booking, error) = await GetBookingForDriverAsync(driverId, bookingId);
        if (error is not null) return new ApiResponse<object> { Message = error };

        if (booking!.Status != BookingStatus.Pending)
            return new ApiResponse<object> { Message = "Booking is no longer pending." };

        booking.Status = BookingStatus.Rejected;
        await _bookingRepository.UpdateAsync(booking);

        // Named event for Flutter
        await _notificationService.SendBookingRejectedAsync(
            booking.PassengerId,
            booking.Id,
            "The driver declined your booking request.");

        // Keep generic in-app + push so the notification matrix still fires
        await _notificationService.SendInAppNotificationAsync(
            booking.PassengerId,
            NotificationType.BookingRejected,
            "Booking Rejected",
            "Your ride booking request was declined by the driver.");

        return ApiResponse<object>.SuccessMessage("Booking rejected.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // NO-SHOW
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Submits a passenger no-show claim for administrator review after the verified pickup arrival grace period.
    /// </summary>
    public async Task<ApiResponse<object>> MarkPassengerNoShowAsync(
        Guid driverId,
        Guid bookingId,
        CancellationToken cancellationToken = default)
    {
        var (_, booking, error) = await GetBookingForDriverAsync(driverId, bookingId, cancellationToken);
        if (error is not null) return new ApiResponse<object> { Message = error };

        if (booking!.Status is not BookingStatus.Accepted and not BookingStatus.Active)
            return new ApiResponse<object> { Message = "Booking must be Accepted or Active to report a no-show." };

        var pickupStop = await _rideRepository.GetStopByIdAsync(booking.PickupStopId, cancellationToken);
        if (pickupStop is null || pickupStop.Status != RideStopStatus.Completed ||
            pickupStop.ArrivedAt is not DateTime arrivedAt ||
            DateTime.UtcNow - arrivedAt < TimeSpan.FromMinutes(AppConstants.NoShowGraceMinutes))
        {
            return new ApiResponse<object> { Message = "No-show grace period has not elapsed." };
        }

        return await _flagService.CreateFlagAsync(driverId, new FlagDto
        {
            ReporteeId = booking.PassengerId,
            RideId = booking.RideId,
            Reason = $"Passenger no-show claim for booking {booking.Id} at pickup stop {pickupStop.Name}."
        }, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // STOP PROGRESSION & END RIDE
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Marks the next stop as active when the driver begins travelling toward it.
    /// </summary>
    public async Task<ApiResponse<object>> BeginStopTravelAsync(Guid driverId, Guid stopId)
    {
        var profile = await _userRepository.GetDriverProfileByUserIdAsync(driverId);
        if (profile is null)
            return new ApiResponse<object> { Message = "Driver profile not found." };

        var stop = await _rideRepository.GetStopByIdAsync(stopId);
        if (stop is null)
            return new ApiResponse<object> { Message = "Stop not found." };

        var ride = await _rideRepository.GetByIdAsync(stop.RideId, includeStops: true);
        if (ride is null || ride.DriverId != profile.Id)
            return new ApiResponse<object> { Message = "Ride not found." };

        if (ride.Status != RideStatus.Active && ride.Status != RideStatus.Posted)
            return new ApiResponse<object> { Message = "Ride is not active." };

        var nextStop = ride.Stops?
            .Where(candidate => candidate.Status != RideStopStatus.Completed)
            .OrderBy(candidate => candidate.SequenceOrder)
            .FirstOrDefault();

        if (nextStop is null || nextStop.Id != stopId)
            return new ApiResponse<object>
            {
                Message = "This stop is not the next expected stop in the sequence."
            };

        if (stop.Status != RideStopStatus.Active)
        {
            stop.Status = RideStopStatus.Active;
            await _rideRepository.UpdateStopAsync(stop);
        }

        return ApiResponse<object>.SuccessMessage("Travel to the next stop started.");
    }

    /// <summary>
    /// Marks a stop as arrived. Validates it is the next un-completed stop in sequence.
    /// Notifies passengers with pickup at this stop.
    /// Generates navigation deep-link to the next stop.
    /// </summary>
    public async Task<ApiResponse<object>> MarkStopArrivedAsync(Guid driverId, Guid stopId)
    {
        const double arrivalRadiusMeters = 150;
        var now = DateTime.UtcNow;
        var profile = await _userRepository.GetDriverProfileByUserIdAsync(driverId);
        if (profile is null)
            return new ApiResponse<object> { Message = "Driver profile not found." };

        var stop = await _rideRepository.GetStopByIdAsync(stopId);
        if (stop is null)
            return new ApiResponse<object> { Message = "Stop not found." };

        var ride = await _rideRepository.GetByIdAsync(stop.RideId, includeStops: true, includeBookings: true);
        if (ride is null || ride.DriverId != profile.Id)
            return new ApiResponse<object> { Message = "Ride not found." };

        if (ride.Status != RideStatus.Active && ride.Status != RideStatus.Posted)
            return new ApiResponse<object> { Message = "Ride is not active." };

        // Validate this is the next stop that has not been reached.
        var nextStop = ride.Stops?
            .Where(s => s.Status != RideStopStatus.Completed)
            .OrderBy(s => s.SequenceOrder)
            .FirstOrDefault();

        if (nextStop is null || nextStop.Id != stopId)
            return new ApiResponse<object>
            {
                Message = "This stop is not the next expected stop in the sequence."
            };

        if (stop.Status != RideStopStatus.Active)
            return new ApiResponse<object>
            {
                Message = "Begin travel to this stop before marking arrival."
            };

        var location = await _rideRepository.GetDriverLocationAsync(profile.Id, ride.Id);
        if (location is null || location.RecordedAt < now.AddSeconds(-30) || location.RecordedAt > now.AddSeconds(5))
        {
            return new ApiResponse<object>
            {
                Message = "A recent driver location is required to confirm arrival."
            };
        }

        var latitudeDelta = (stop.Latitude - location.Latitude) * (Math.PI / 180);
        var longitudeDelta = (stop.Longitude - location.Longitude) * (Math.PI / 180);
        var startLatitude = location.Latitude * (Math.PI / 180);
        var stopLatitude = stop.Latitude * (Math.PI / 180);
        var haversine = Math.Pow(Math.Sin(latitudeDelta / 2), 2)
                        + Math.Cos(startLatitude) * Math.Cos(stopLatitude)
                        * Math.Pow(Math.Sin(longitudeDelta / 2), 2);
        var distanceMeters = 6_371_000 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(haversine, 0, 1)));
        if (distanceMeters > arrivalRadiusMeters)
        {
            return new ApiResponse<object>
            {
                Message = "Driver must be within 150 meters of the stop to confirm arrival."
            };
        }

        stop.Status = RideStopStatus.Completed;
        stop.ArrivedAt = now;
        await _rideRepository.UpdateStopAsync(stop);

        // Mark ride as Active on first stop arrival
        if (ride.Status == RideStatus.Posted)
        {
            ride.Status = RideStatus.Active;
            ride.StartedAt = DateTime.UtcNow;
            await _rideRepository.UpdateAsync(ride);
        }

        // Notify passengers whose pickup stop is this one; promote Accepted → Active (chat enabled)
        var passengersAtStop = ride.Bookings?
            .Where(b => b.PickupStopId == stopId &&
                        (b.Status == BookingStatus.Accepted || b.Status == BookingStatus.Active))
            .ToList() ?? new List<Booking>();

        // Named event to ride group (one broadcast covers all passengers at this stop)
        await _notificationService.SendDriverArrivedAtStopAsync(stop.RideId, stop.Name, stop.Id);

        foreach (var booking in passengersAtStop)
        {
            if (booking.Status == BookingStatus.Accepted)
            {
                booking.Status = BookingStatus.Active;
                _context.Entry(booking).State = EntityState.Modified;
            }

            // Keep generic in-app + push so the notification matrix still fires
            await _notificationService.SendInAppNotificationAsync(
                booking.PassengerId,
                NotificationType.DriverArrivedAtStop,
                "Driver Arrived",
                $"Your driver has arrived at {stop.Name}. Please head to the pickup point.");
        }
        await _context.SaveChangesAsync();

        // Build navigation deep-link for driver to next stop
        object? navigationData = null;
        var nextStopAfter = ride.Stops?
            .Where(s => s.Status != RideStopStatus.Completed && s.Id != stopId)
            .OrderBy(s => s.SequenceOrder)
            .FirstOrDefault();

        if (nextStopAfter is not null)
        {
            var driverLocation = await _rideRepository.GetDriverLocationAsync(profile.Id, ride.Id);
            if (driverLocation is not null)
            {
                var navLink = $"https://maps.google.com/?saddr={driverLocation.Latitude},{driverLocation.Longitude}" +
                              $"&daddr={nextStopAfter.Latitude},{nextStopAfter.Longitude}";
                navigationData = new
                {
                    nextStopName = nextStopAfter.Name,
                    navigationLink = navLink
                };

                await _hubContext.Clients
                    .Group($"user-{driverId}")
                    .SendAsync("NavigationLinkUpdated", navigationData);
            }
        }

        return ApiResponse<object>.Success(
            navigationData ?? new { message = "All stops completed." },
            $"Arrived at {stop.Name}.");
    }

    /// <summary>
    /// Ends the ride for a specific passenger. Sets booking to Completed.
    /// If all bookings are completed/cancelled, marks the ride as Completed.
    /// </summary>
    public async Task<ApiResponse<object>> EndRideForPassengerAsync(Guid driverId, Guid bookingId)
    {
        var (profile, booking, error) = await GetBookingForDriverAsync(driverId, bookingId);
        if (error is not null) return new ApiResponse<object> { Message = error };

        if (booking!.Status != BookingStatus.Accepted && booking.Status != BookingStatus.Active)
            return new ApiResponse<object> { Message = "Booking is not in an active state." };

        var dropoffStop = await _rideRepository.GetStopByIdAsync(booking.DropoffStopId);
        if (dropoffStop is null || dropoffStop.Status != RideStopStatus.Completed)
            return new ApiResponse<object> { Message = "Dropoff stop has not been reached yet." };

        booking.Status = BookingStatus.Completed;
        booking.CompletedAt = DateTime.UtcNow;
        await _bookingRepository.UpdateAsync(booking);

        var ride = await _rideRepository.GetByIdAsync(booking.RideId);

        // Named event for Flutter (structured payload with fare)
        await _notificationService.SendRideEndedForPassengerAsync(
            booking.PassengerId,
            booking.Id,
            ride?.Fare ?? 0m);

        // Keep generic in-app + push so the notification matrix still fires
        await _notificationService.SendInAppNotificationAsync(
            booking.PassengerId,
            NotificationType.RideEnded,
            "Ride Ended",
            $"Your ride has ended. Please pay PKR {ride?.Fare:F0} in cash.");

        // Award ride completion trust bonus to driver
        await _trustService.AddPointsAsync(driverId, AppConstants.RideCompletionBonus);

        // Check if all bookings are done → complete the ride
        if (ride is not null)
        {
            var allDone = await _bookingRepository.AreAllBookingsCompletedOrCancelledAsync(ride.Id);
            if (allDone)
            {
                ride.Status = RideStatus.Completed;
                ride.CompletedAt = DateTime.UtcNow;
                await _rideRepository.UpdateAsync(ride);
            }
        }

        return ApiResponse<object>.SuccessMessage("Ride ended for passenger.");
    }

    /// <summary>
    /// Confirms cash payment received from a passenger.
    /// Triggers the ratings flow for both driver and passenger.
    /// </summary>
    public async Task<ApiResponse<object>> ConfirmCashReceivedAsync(Guid driverId, Guid bookingId)
    {
        var (_, booking, error) = await GetBookingForDriverAsync(driverId, bookingId);
        if (error is not null) return new ApiResponse<object> { Message = error };

        if (booking!.Status != BookingStatus.Completed)
            return new ApiResponse<object> { Message = "Booking must be completed before confirming cash." };

        booking.IsCashPaid = true;
        await _bookingRepository.UpdateAsync(booking);

        // Named event for Flutter
        await _notificationService.SendCashReceivedConfirmedAsync(booking.PassengerId, booking.Id);

        // Keep generic in-app + push so the notification matrix still fires
        await _notificationService.SendInAppNotificationAsync(
            booking.PassengerId,
            NotificationType.CashReceived,
            "Cash Confirmed",
            "Driver has confirmed receipt of your cash payment. Please rate your ride experience.");

        return ApiResponse<object>.SuccessMessage("Cash receipt confirmed. Rating flow started.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // LOCATION TRACKING
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates driver GPS location. Upsert pattern: one record per driver per ride.
    /// Broadcasts location update to all passengers in the ride group via SignalR.
    /// </summary>
    public async Task<ApiResponse<object>> UpdateLocationAsync(Guid driverId, DriverLocationDto dto)
    {
        if (!double.IsFinite(dto.Lat) || !double.IsFinite(dto.Lng)
            || dto.Lat is < -90 or > 90 || dto.Lng is < -180 or > 180)
        {
            return new ApiResponse<object> { Message = "Location coordinates are invalid." };
        }

        var profile = await _userRepository.GetDriverProfileByUserIdAsync(driverId);
        if (profile is null)
            return new ApiResponse<object> { Message = "Driver profile not found." };

        var ride = await _rideRepository.GetByIdAsync(dto.RideId);
        if (ride is null)
            return new ApiResponse<object> { Message = "Ride not found." };

        if (ride.DriverId != profile.Id)
            return new ApiResponse<object> { Message = "You are not authorized to update location for this ride." };

        if (ride.Status != RideStatus.Active)
            return new ApiResponse<object> { Message = "Location can only be updated for an active ride." };

        var location = new DriverLocation
        {
            Id = Guid.NewGuid(),
            DriverId = profile.Id,
            RideId = dto.RideId,
            Latitude = dto.Lat,
            Longitude = dto.Lng,
            RecordedAt = DateTime.UtcNow
        };

        await _rideRepository.UpsertDriverLocationAsync(location);

        // Broadcast to all in the ride group
        await _hubContext.Clients
            .Group($"ride-{dto.RideId}")
            .SendAsync("ReceiveLocationUpdate", new
            {
                lat = dto.Lat,
                lng = dto.Lng,
                timestamp = DateTime.UtcNow
            });

        return ApiResponse<object>.SuccessMessage("Location updated.");
    }

    /// <summary>
    /// Notifies a specific passenger that the driver has arrived at their pickup location.
    /// Broadcasts 'DriverIsHere' via SignalR.
    /// </summary>
    public async Task<ApiResponse<object>> NotifyIAmHereAsync(Guid driverId, Guid bookingId)
    {
        var (profile, booking, error) = await GetBookingForDriverAsync(driverId, bookingId);
        if (error is not null) return new ApiResponse<object> { Message = error };

        // Ensure the booking is in an accepted state (or active if they somehow click it after arrival)
        if (booking!.Status != BookingStatus.Accepted && booking.Status != BookingStatus.Active)
            return new ApiResponse<object> { Message = "Booking must be in Accepted or Active state to send arrival notification." };

        var driverUser = await _userRepository.GetByIdAsync(driverId);
        var driverName = driverUser?.FullName ?? "Your driver";

        await _hubContext.Clients
            .Group($"user-{booking.PassengerId}")
            .SendAsync("DriverIsHere", new
            {
                bookingId = booking.Id,
                driverName = driverName
            });

        return ApiResponse<object>.SuccessMessage("Arrival notification sent to passenger.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<(DriverProfile? profile, Booking? booking, string? error)>
        GetBookingForDriverAsync(Guid driverId, Guid bookingId, CancellationToken cancellationToken = default)
    {
        var profile = await _userRepository.GetDriverProfileByUserIdAsync(driverId, cancellationToken);
        if (profile is null) return (null, null, "Driver profile not found.");

        var booking = await _bookingRepository.GetByIdAsync(
            bookingId,
            includeRide: true,
            cancellationToken: cancellationToken);
        if (booking is null) return (null, null, "Booking not found.");

        var ride = await _rideRepository.GetByIdAsync(booking.RideId, cancellationToken: cancellationToken);
        if (ride is null || ride.DriverId != profile.Id)
            return (null, null, "Booking does not belong to this driver.");

        return (profile, booking, null);
    }

    private static CarStatusResponseDto MapCarToStatusDto(DriverCar car) =>
        new()
        {
            Id = car.Id,
            Make = car.Make,
            Model = car.Model,
            Year = car.Year,
            Status = car.Status.ToString(),
            RejectionReason = car.RejectionReason,
            ApprovedAt = car.ApprovedAt
        };

    private async Task<RideResponseDto> MapRideToResponseDtoAsync(Ride ride, DriverProfile profile)
    {
        var user = await _userRepository.GetByIdAsync(profile.UserId);
        return new RideResponseDto
        {
            Id = ride.Id,
            DriverId = profile.Id,
            DriverName = user?.FullName ?? "Unknown",
            DriverRating = profile.AverageRating,
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
                    ArrivedAt = s.ArrivedAt
                })
                .ToList()
        };
    }
}
