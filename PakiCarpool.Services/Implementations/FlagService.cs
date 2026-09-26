using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.Services.Implementations;

/// <summary>
/// Creates user-submitted flags/reports against another user for a specific ride.
/// Flags default to Open and are resolved by admins as Dismissed or ActionTaken.
/// </summary>
public class FlagService : IFlagService
{
    private readonly IAdminRepository _adminRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRideRepository _rideRepository;
    private readonly IBookingRepository _bookingRepository;

    public FlagService(
        IAdminRepository adminRepository,
        IUserRepository userRepository,
        IRideRepository rideRepository,
        IBookingRepository bookingRepository)
    {
        _adminRepository = adminRepository;
        _userRepository = userRepository;
        _rideRepository = rideRepository;
        _bookingRepository = bookingRepository;
    }

    /// <summary>
    /// Creates a flag/report against another user for a specific ride.
    /// Reporter and reportee must both be participants in the referenced ride.
    /// </summary>
    public async Task<ApiResponse<object>> CreateFlagAsync(Guid reporterId, FlagDto dto)
    {
        if (reporterId == dto.ReporteeId)
            return new ApiResponse<object> { Message = "You cannot flag yourself." };

        var reporter = await _userRepository.GetByIdAsync(reporterId);
        var reportee = await _userRepository.GetByIdAsync(dto.ReporteeId);
        if (reporter is null || reportee is null)
            return new ApiResponse<object> { Message = ErrorCodes.NotFound };

        var ride = await _rideRepository.GetByIdAsync(dto.RideId, includeBookings: true);
        if (ride is null)
            return new ApiResponse<object> { Message = ErrorCodes.NotFound };

        var reporterIsParticipant = await IsRideParticipantAsync(ride, reporterId);
        var reporteeIsParticipant = await IsRideParticipantAsync(ride, dto.ReporteeId);

        if (!reporterIsParticipant || !reporteeIsParticipant)
            return new ApiResponse<object>
            {
                Message = "Both users must be participants in the referenced ride."
            };

        if (string.IsNullOrWhiteSpace(dto.Reason) || dto.Reason.Trim().Length < AppConstants.MinCancelReasonLength)
            return new ApiResponse<object>
            {
                Message = $"Flag reason must be at least {AppConstants.MinCancelReasonLength} characters."
            };

        var flag = new Flag
        {
            Id = Guid.NewGuid(),
            ReporterId = reporterId,
            ReporteeId = dto.ReporteeId,
            RideId = dto.RideId,
            Reason = dto.Reason.Trim(),
            Status = FlagStatus.Open,
            CreatedAt = DateTime.UtcNow
        };

        await _adminRepository.CreateFlagAsync(flag);

        return ApiResponse<object>.Success(
            new { flagId = flag.Id },
            "Flag submitted. An admin will review it shortly.");
    }

    private async Task<bool> IsRideParticipantAsync(Ride ride, Guid userId)
    {
        var driverProfile = ride.Driver
            ?? await _userRepository.GetDriverProfileByIdAsync(ride.DriverId);

        if (driverProfile is not null && driverProfile.UserId == userId)
            return true;

        var bookings = ride.Bookings?.ToList()
            ?? await _bookingRepository.GetBookingsByRideIdAsync(ride.Id);

        return bookings.Any(b => b.PassengerId == userId);
    }
}
