using System.Globalization;
using System.Text;
using System.Text.Json;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Admin;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.DTOs.Driver;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.Services.Implementations;

/// <summary>
/// Implements all admin portal business operations:
/// dashboard metrics, user management, car approvals, ride oversight, flag resolution, and reports.
/// Admin accounts are seeded only — this service never creates Admin users.
/// </summary>
public class AdminService : IAdminService
{
    private readonly IUserRepository _userRepository;
    private readonly IRideRepository _rideRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly IAdminRepository _adminRepository;
    private readonly INotificationService _notificationService;
    private readonly IEmailService _emailService;

    public AdminService(
        IUserRepository userRepository,
        IRideRepository rideRepository,
        IBookingRepository bookingRepository,
        IAdminRepository adminRepository,
        INotificationService notificationService,
        IEmailService emailService)
    {
        _userRepository = userRepository;
        _rideRepository = rideRepository;
        _bookingRepository = bookingRepository;
        _adminRepository = adminRepository;
        _notificationService = notificationService;
        _emailService = emailService;
    }

    /// <summary>
    /// Returns summary dashboard metrics for the admin portal.
    /// </summary>
    public async Task<ApiResponse<DashboardDto>> GetDashboardAsync()
    {
        var totalDrivers = await _userRepository.CountByRoleAsync(UserRole.Driver);
        var totalPassengers = await _userRepository.CountByRoleAsync(UserRole.Passenger);
        var totalAdmins = await _userRepository.CountByRoleAsync(UserRole.Admin);
        var pendingCars = await _userRepository.GetCarsByStatusAsync(CarStatus.Pending);

        var dashboard = new DashboardDto
        {
            TotalUsers = totalDrivers + totalPassengers + totalAdmins,
            TotalDrivers = totalDrivers,
            TotalPassengers = totalPassengers,
            TotalRides = await _rideRepository.CountTotalRidesAsync(),
            ActiveRides = await _rideRepository.CountActiveRidesAsync(),
            PendingCarApprovals = pendingCars.Count,
            OpenFlags = await _adminRepository.CountOpenFlagsAsync(),
            SuspendedAccounts = await _userRepository.CountSuspendedAsync()
        };

        return ApiResponse<DashboardDto>.Success(dashboard);
    }

    /// <summary>
    /// Lists all users with pagination.
    /// </summary>
    public async Task<ApiResponse<PaginatedResult<UserListItemDto>>> GetUsersAsync(PaginationParams pagination)
    {
        var (items, totalCount) = await _userRepository.GetAllPaginatedAsync(
            pagination.Page, pagination.PageSize);

        var mapped = items.Select(u => new UserListItemDto
        {
            Id = u.Id,
            FullName = u.FullName,
            Email = u.Email,
            Role = u.Role,
            IsActive = u.IsActive,
            IsSuspended = u.IsSuspended,
            TrustScore = u.TrustScore,
            CreatedAt = u.CreatedAt
        }).ToList();

        return ApiResponse<PaginatedResult<UserListItemDto>>.Success(
            PaginatedResult<UserListItemDto>.Create(mapped, totalCount, pagination.Page, pagination.PageSize));
    }

    /// <summary>
    /// Returns full user profile details including driver profile and cars if applicable.
    /// </summary>
    public async Task<ApiResponse<UserDetailDto>> GetUserDetailAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user is null)
            return new ApiResponse<UserDetailDto> { Message = ErrorCodes.NotFound };

        var detail = new UserDetailDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role,
            IsActive = user.IsActive,
            IsSuspended = user.IsSuspended,
            TrustScore = user.TrustScore,
            IsEmailVerified = user.IsEmailVerified,
            CreatedAt = user.CreatedAt
        };

        if (user.Role == UserRole.Driver)
        {
            var profile = await _userRepository.GetDriverProfileByUserIdAsync(user.Id);
            if (profile is not null)
            {
                var cars = await _userRepository.GetDriverCarsAsync(profile.Id);
                detail.DriverProfile = new DriverProfileDetailDto
                {
                    Id = profile.Id,
                    LivenessVerified = profile.LivenessVerified,
                    ProfilePhotoPath = profile.ProfilePhotoPath,
                    LivenessFailCount = profile.LivenessFailCount,
                    AverageRating = profile.AverageRating,
                    Cars = cars.Select(c => MapCarApproval(c, user.FullName)).ToList()
                };
            }
        }

        return ApiResponse<UserDetailDto>.Success(detail);
    }

    /// <summary>
    /// Blocks a user account. Sets IsActive = false and IsSuspended = true.
    /// </summary>
    public async Task<ApiResponse<object>> BlockUserAsync(Guid userId, BlockUserDto dto)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user is null)
            return new ApiResponse<object> { Message = ErrorCodes.NotFound };

        if (user.Role == UserRole.Admin)
            return new ApiResponse<object> { Message = "Admin accounts cannot be blocked via this endpoint." };

        user.IsActive = false;
        user.IsSuspended = true;
        await _userRepository.UpdateAsync(user);
        await _userRepository.RevokeAllRefreshTokensAsync(userId);

        await _notificationService.SendInAppNotificationAsync(
            userId, NotificationType.AccountSuspended, "Account Blocked", dto.Reason);
        await _emailService.SendSuspensionEmailAsync(user.Email, user.FullName, dto.Reason);

        return ApiResponse<object>.SuccessMessage("User blocked successfully.");
    }

    /// <summary>
    /// Unblocks a user account. Sets IsActive = true, IsSuspended = false.
    /// </summary>
    public async Task<ApiResponse<object>> UnblockUserAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user is null)
            return new ApiResponse<object> { Message = ErrorCodes.NotFound };

        user.IsActive = true;
        user.IsSuspended = false;
        await _userRepository.UpdateAsync(user);

        return ApiResponse<object>.SuccessMessage("User unblocked successfully.");
    }

    /// <summary>
    /// Lists all pending car registrations awaiting admin approval.
    /// </summary>
    public async Task<ApiResponse<List<CarApprovalDto>>> GetPendingCarApprovalsAsync()
    {
        var pendingCars = await _userRepository.GetCarsByStatusAsync(CarStatus.Pending);
        var result = new List<CarApprovalDto>();

        foreach (var car in pendingCars)
        {
            var profile = await _userRepository.GetDriverProfileByIdAsync(car.DriverId);
            var driverName = "Unknown";
            if (profile is not null)
            {
                var user = await _userRepository.GetByIdAsync(profile.UserId);
                driverName = user?.FullName ?? "Unknown";
            }

            result.Add(MapCarApproval(car, driverName));
        }

        return ApiResponse<List<CarApprovalDto>>.Success(result);
    }

    /// <summary>
    /// Approves a car registration. Notifies the driver in-app and by email.
    /// </summary>
    public async Task<ApiResponse<object>> ApproveCarAsync(Guid carId)
    {
        var car = await _userRepository.GetDriverCarByIdAsync(carId);
        if (car is null)
            return new ApiResponse<object> { Message = ErrorCodes.NotFound };

        if (car.Status != CarStatus.Pending)
            return new ApiResponse<object> { Message = "Only pending cars can be approved." };

        car.Status = CarStatus.Approved;
        car.ApprovedAt = DateTime.UtcNow;
        car.RejectionReason = null;
        await _userRepository.UpdateDriverCarAsync(car);

        var profile = await _userRepository.GetDriverProfileByIdAsync(car.DriverId);
        if (profile is not null)
        {
            var user = await _userRepository.GetByIdAsync(profile.UserId);
            if (user is not null)
            {
                await _notificationService.SendNotificationAsync(
                    user.Id, NotificationType.CarApproved,
                    "Car Approved",
                    $"Your {car.Make} {car.Model} has been approved. You can now post rides.");
                await _emailService.SendCarApprovedEmailAsync(
                    user.Email, user.FullName, car.Make, car.Model);
            }
        }

        return ApiResponse<object>.SuccessMessage("Car approved successfully.");
    }

    /// <summary>
    /// Rejects a car registration with a reason. Notifies the driver in-app and by email.
    /// </summary>
    public async Task<ApiResponse<object>> RejectCarAsync(Guid carId, RejectCarDto dto)
    {
        var car = await _userRepository.GetDriverCarByIdAsync(carId);
        if (car is null)
            return new ApiResponse<object> { Message = ErrorCodes.NotFound };

        if (car.Status != CarStatus.Pending)
            return new ApiResponse<object> { Message = "Only pending cars can be rejected." };

        car.Status = CarStatus.Rejected;
        car.RejectionReason = dto.RejectionReason;
        car.ApprovedAt = null;
        await _userRepository.UpdateDriverCarAsync(car);

        var profile = await _userRepository.GetDriverProfileByIdAsync(car.DriverId);
        if (profile is not null)
        {
            var user = await _userRepository.GetByIdAsync(profile.UserId);
            if (user is not null)
            {
                await _notificationService.SendNotificationAsync(
                    user.Id, NotificationType.CarRejected,
                    "Car Rejected",
                    $"Your {car.Make} {car.Model} was rejected: {dto.RejectionReason}");
                await _emailService.SendCarRejectedEmailAsync(
                    user.Email, user.FullName, car.Make, car.Model, dto.RejectionReason);
            }
        }

        return ApiResponse<object>.SuccessMessage("Car rejected.");
    }

    /// <summary>
    /// Manually verifies a driver's liveness (admin override after failed checks).
    /// </summary>
    public async Task<ApiResponse<object>> ManualVerifyDriverAsync(Guid driverId)
    {
        var profile = await _userRepository.GetDriverProfileByIdAsync(driverId)
                      ?? await _userRepository.GetDriverProfileByUserIdAsync(driverId);

        if (profile is null)
            return new ApiResponse<object> { Message = ErrorCodes.NotFound };

        profile.LivenessVerified = true;
        profile.LivenessFailCount = 0;
        profile.LivenessBannedUntil = null;
        await _userRepository.UpdateDriverProfileAsync(profile);

        return ApiResponse<object>.SuccessMessage("Driver liveness manually verified.");
    }

    /// <summary>
    /// Lists all rides with pagination (admin view).
    /// </summary>
    public async Task<ApiResponse<PaginatedResult<RideResponseDto>>> GetRidesAsync(PaginationParams pagination)
    {
        var (items, totalCount) = await _rideRepository.GetAllPaginatedAsync(
            pagination.Page, pagination.PageSize);

        var mapped = items.Select(MapRideToResponseDto).ToList();
        return ApiResponse<PaginatedResult<RideResponseDto>>.Success(
            PaginatedResult<RideResponseDto>.Create(mapped, totalCount, pagination.Page, pagination.PageSize));
    }

    /// <summary>
    /// Admin force-cancels any ride and notifies all Accepted passengers.
    /// </summary>
    public async Task<ApiResponse<object>> CancelRideAsync(Guid rideId)
    {
        var ride = await _rideRepository.GetByIdAsync(rideId, includeStops: true, includeBookings: true);
        if (ride is null)
            return new ApiResponse<object> { Message = ErrorCodes.NotFound };

        if (ride.Status is RideStatus.Completed or RideStatus.Cancelled)
            return new ApiResponse<object> { Message = "Ride is already completed or cancelled." };

        ride.Status = RideStatus.Cancelled;
        ride.CancelReason = "Cancelled by platform administrator";
        await _rideRepository.UpdateAsync(ride);

        var accepted = ride.Bookings?
            .Where(b => b.Status is BookingStatus.Accepted or BookingStatus.Active or BookingStatus.Pending)
            .ToList() ?? new List<Booking>();

        foreach (var booking in accepted)
        {
            booking.Status = BookingStatus.Cancelled;
            booking.CancelReason = "Ride cancelled by administrator";
            await _bookingRepository.UpdateAsync(booking);

            await _notificationService.SendNotificationAsync(
                booking.PassengerId,
                NotificationType.RideCancelledByDriver,
                "Ride Cancelled",
                "Your ride was cancelled by the platform administrator.");

            var passenger = await _userRepository.GetByIdAsync(booking.PassengerId);
            if (passenger is not null)
            {
                await _emailService.SendRideCancelledEmailAsync(
                    passenger.Email, passenger.FullName,
                    $"Admin cancellation — ride {ride.Id}");
            }
        }

        var driverProfile = await _userRepository.GetDriverProfileByIdAsync(ride.DriverId);
        if (driverProfile is not null)
        {
            await _notificationService.SendInAppNotificationAsync(
                driverProfile.UserId,
                NotificationType.RideCancelledByDriver,
                "Ride Cancelled by Admin",
                "Your ride was cancelled by a platform administrator.");
        }

        return ApiResponse<object>.SuccessMessage("Ride cancelled by admin.");
    }

    /// <summary>
    /// Lists all flags with pagination.
    /// </summary>
    public async Task<ApiResponse<PaginatedResult<FlagListItemDto>>> GetFlagsAsync(PaginationParams pagination)
    {
        var (items, totalCount) = await _adminRepository.GetFlagsPaginatedAsync(
            pagination.Page, pagination.PageSize);

        var result = new List<FlagListItemDto>();
        foreach (var flag in items)
        {
            var reporter = await _userRepository.GetByIdAsync(flag.ReporterId);
            var reportee = await _userRepository.GetByIdAsync(flag.ReporteeId);

            result.Add(new FlagListItemDto
            {
                Id = flag.Id,
                ReporterId = flag.ReporterId,
                ReporterName = reporter?.FullName ?? "Unknown",
                ReporteeId = flag.ReporteeId,
                ReporteeName = reportee?.FullName ?? "Unknown",
                RideId = flag.RideId,
                Reason = flag.Reason,
                Status = flag.Status,
                CreatedAt = flag.CreatedAt
            });
        }

        return ApiResponse<PaginatedResult<FlagListItemDto>>.Success(
            PaginatedResult<FlagListItemDto>.Create(result, totalCount, pagination.Page, pagination.PageSize));
    }

    /// <summary>
    /// Dismisses a flag with optional notes.
    /// </summary>
    public async Task<ApiResponse<object>> DismissFlagAsync(Guid flagId, DismissFlagDto dto)
    {
        var flag = await _adminRepository.GetFlagByIdAsync(flagId);
        if (flag is null)
            return new ApiResponse<object> { Message = ErrorCodes.NotFound };

        if (flag.Status != FlagStatus.Open)
            return new ApiResponse<object> { Message = "Only open flags can be dismissed." };

        flag.Status = FlagStatus.Dismissed;
        await _adminRepository.UpdateFlagAsync(flag);

        return ApiResponse<object>.SuccessMessage(
            string.IsNullOrWhiteSpace(dto.Notes)
                ? "Flag dismissed."
                : $"Flag dismissed. Notes: {dto.Notes}");
    }

    /// <summary>
    /// Takes action on a flag (warn or block the reportee).
    /// </summary>
    public async Task<ApiResponse<object>> ActionFlagAsync(Guid flagId, ActionFlagDto dto)
    {
        var flag = await _adminRepository.GetFlagByIdAsync(flagId);
        if (flag is null)
            return new ApiResponse<object> { Message = ErrorCodes.NotFound };

        if (flag.Status != FlagStatus.Open)
            return new ApiResponse<object> { Message = "Only open flags can be actioned." };

        flag.Status = FlagStatus.ActionTaken;
        await _adminRepository.UpdateFlagAsync(flag);

        var action = dto.Action.Trim().ToLowerInvariant();
        var reportee = await _userRepository.GetByIdAsync(flag.ReporteeId);

        if (reportee is not null)
        {
            if (action.Contains("block") || action.Contains("suspend"))
            {
                reportee.IsActive = false;
                reportee.IsSuspended = true;
                await _userRepository.UpdateAsync(reportee);
                await _userRepository.RevokeAllRefreshTokensAsync(reportee.Id);

                await _notificationService.SendInAppNotificationAsync(
                    reportee.Id, NotificationType.AccountSuspended,
                    "Account Suspended", dto.Action);
                await _emailService.SendSuspensionEmailAsync(
                    reportee.Email, reportee.FullName, dto.Action);
            }
            else
            {
                await _notificationService.SendInAppNotificationAsync(
                    reportee.Id, NotificationType.WarningIssued,
                    "Warning Issued", dto.Action);
                await _emailService.SendWarningEmailAsync(
                    reportee.Email, reportee.FullName, dto.Action);
            }
        }

        return ApiResponse<object>.SuccessMessage("Flag action applied.");
    }

    /// <summary>
    /// Generates admin report summary with optional date filtering.
    /// </summary>
    public async Task<ApiResponse<object>> GetReportsAsync(DateTime? from, DateTime? to)
    {
        var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
        var toDate = to ?? DateTime.UtcNow;

        var dashboard = (await GetDashboardAsync()).Data;
        var (rides, rideCount) = await _rideRepository.GetAllPaginatedAsync(1, 50);

        var ridesInRange = rides
            .Where(r => r.CreatedAt >= fromDate && r.CreatedAt <= toDate)
            .ToList();

        var report = new
        {
            from = fromDate,
            to = toDate,
            summary = dashboard,
            ridesInRange = ridesInRange.Count,
            completedInRange = ridesInRange.Count(r => r.Status == RideStatus.Completed),
            cancelledInRange = ridesInRange.Count(r => r.Status == RideStatus.Cancelled),
            totalRidesSampled = rideCount,
            rides = ridesInRange.Select(MapRideToResponseDto).ToList()
        };

        return ApiResponse<object>.Success(report, "Report generated.");
    }

    /// <summary>
    /// Exports reports as CSV or a simple PDF document.
    /// </summary>
    public async Task<byte[]> ExportReportsAsync(DateTime? from, DateTime? to, string format)
    {
        var reportResponse = await GetReportsAsync(from, to);
        var payload = JsonSerializer.Serialize(reportResponse.Data, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var normalized = (format ?? "csv").Trim().ToLowerInvariant();
        if (normalized == "pdf")
            return BuildSimplePdf("Paki Carpool Report", payload);

        var sb = new StringBuilder();
        sb.AppendLine("Paki Carpool Platform Report");
        sb.AppendLine($"Generated,{DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"From,{(from ?? DateTime.UtcNow.AddDays(-30)).ToString("O", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"To,{(to ?? DateTime.UtcNow).ToString("O", CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine("ReportJson");
        sb.AppendLine($"\"{payload.Replace("\"", "\"\"")}\"");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static CarApprovalDto MapCarApproval(DriverCar car, string driverName)
    {
        var photos = new List<string>();
        try
        {
            photos = JsonSerializer.Deserialize<List<string>>(car.PhotoPaths) ?? new List<string>();
        }
        catch
        {
            photos = new List<string>();
        }

        return new CarApprovalDto
        {
            Id = car.Id,
            DriverId = car.DriverId,
            DriverName = driverName,
            Make = car.Make,
            Model = car.Model,
            Year = car.Year,
            AvailableSeats = car.AvailableSeats,
            Status = car.Status.ToString(),
            RegDocPath = car.RegDocPath,
            PhotoPaths = photos,
            CreatedAt = car.CreatedAt
        };
    }

    private static RideResponseDto MapRideToResponseDto(Ride ride)
    {
        return new RideResponseDto
        {
            Id = ride.Id,
            DriverId = ride.DriverId,
            DriverName = ride.Driver?.User?.FullName ?? "Unknown",
            DriverRating = ride.Driver?.AverageRating ?? 0m,
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
                    ArrivedAt = s.ArrivedAt
                })
                .ToList()
        };
    }

    /// <summary>
    /// Builds a minimal single-page PDF without third-party libraries.
    /// </summary>
    private static byte[] BuildSimplePdf(string title, string body)
    {
        var safeBody = body
            .Replace("\\", "\\\\")
            .Replace("(", "\\(")
            .Replace(")", "\\)")
            .Replace("\r", string.Empty);

        var lines = safeBody.Split('\n').Take(40).ToList();
        var content = new StringBuilder();
        content.AppendLine("BT");
        content.AppendLine("/F1 14 Tf");
        content.AppendLine("50 780 Td");
        content.AppendLine($"({title.Replace("(", "\\(").Replace(")", "\\)")}) Tj");
        content.AppendLine("/F1 9 Tf");
        content.AppendLine("0 -24 Td");

        foreach (var line in lines)
        {
            var truncated = line.Length > 90 ? line[..90] : line;
            content.AppendLine($"({truncated}) Tj");
            content.AppendLine("0 -12 Td");
        }

        content.AppendLine("ET");
        var stream = content.ToString();
        var streamBytes = Encoding.ASCII.GetBytes(stream);

        var objects = new List<string>
        {
            "1 0 obj<< /Type /Catalog /Pages 2 0 R >>endobj\n",
            "2 0 obj<< /Type /Pages /Kids [3 0 R] /Count 1 >>endobj\n",
            "3 0 obj<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources<< /Font<< /F1 5 0 R >> >> >>endobj\n",
            $"4 0 obj<< /Length {streamBytes.Length} >>stream\n{stream}endstream\nendobj\n",
            "5 0 obj<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>endobj\n"
        };

        var pdf = new StringBuilder();
        pdf.Append("%PDF-1.4\n");
        var offsets = new List<int> { 0 };

        foreach (var obj in objects)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(obj);
        }

        var xrefPos = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objects.Count + 1}\n");
        pdf.Append("0000000000 65535 f \n");
        for (var i = 1; i < offsets.Count; i++)
            pdf.Append($"{offsets[i]:D10} 00000 n \n");

        pdf.Append($"trailer<< /Size {objects.Count + 1} /Root 1 0 R >>\n");
        pdf.Append($"startxref\n{xrefPos}\n%%EOF");

        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
