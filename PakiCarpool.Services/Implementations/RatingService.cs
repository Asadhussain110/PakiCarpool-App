using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.Services.Implementations;

/// <summary>
/// Submits post-ride ratings after cash payment is confirmed.
/// Positive ratings (4–5 stars) add +3 trust points; low ratings (1–2) deduct 2 points.
/// One rating per rater–ratee pair per ride.
/// </summary>
public class RatingService : IRatingService
{
    private readonly IAdminRepository _adminRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRideRepository _rideRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly ITrustService _trustService;

    public RatingService(
        IAdminRepository adminRepository,
        IUserRepository userRepository,
        IRideRepository rideRepository,
        IBookingRepository bookingRepository,
        ITrustService trustService)
    {
        _adminRepository = adminRepository;
        _userRepository = userRepository;
        _rideRepository = rideRepository;
        _bookingRepository = bookingRepository;
        _trustService = trustService;
    }

    /// <summary>
    /// Submits a rating for a ride participant. Stars 1–5, optional review text (max 300 chars).
    /// Positive ratings (4–5 stars) add +3 trust points to the ratee.
    /// </summary>
    public async Task<ApiResponse<object>> SubmitRatingAsync(Guid raterId, RatingDto dto)
    {
        if (raterId == dto.RateeId)
            return new ApiResponse<object> { Message = "You cannot rate yourself." };

        if (dto.Stars < AppConstants.MinRatingStars || dto.Stars > AppConstants.MaxRatingStars)
            return new ApiResponse<object> { Message = ErrorCodes.ValidationError };

        if (dto.ReviewText is not null && dto.ReviewText.Length > AppConstants.MaxReviewTextLength)
            return new ApiResponse<object>
            {
                Message = $"Review text cannot exceed {AppConstants.MaxReviewTextLength} characters."
            };

        var rater = await _userRepository.GetByIdAsync(raterId);
        var ratee = await _userRepository.GetByIdAsync(dto.RateeId);
        if (rater is null || ratee is null)
            return new ApiResponse<object> { Message = ErrorCodes.NotFound };

        var ride = await _rideRepository.GetByIdAsync(dto.RideId, includeBookings: true);
        if (ride is null)
            return new ApiResponse<object> { Message = ErrorCodes.NotFound };

        if (!await CanRateAsync(ride, raterId, dto.RateeId))
            return new ApiResponse<object>
            {
                Message = "Rating is only allowed after cash payment for a completed booking on this ride."
            };

        if (await _adminRepository.RatingExistsAsync(dto.RideId, raterId, dto.RateeId))
            return new ApiResponse<object> { Message = "You have already rated this user for this ride." };

        var rating = new Rating
        {
            Id = Guid.NewGuid(),
            RideId = dto.RideId,
            RaterId = raterId,
            RateeId = dto.RateeId,
            Stars = dto.Stars,
            ReviewText = string.IsNullOrWhiteSpace(dto.ReviewText) ? null : dto.ReviewText.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        await _adminRepository.CreateRatingAsync(rating);

        if (dto.Stars >= AppConstants.PositiveRatingMinStars)
        {
            await _trustService.AddPointsAsync(dto.RateeId, AppConstants.PositiveRatingBonus);
        }
        else if (dto.Stars <= 2)
        {
            // PRD: Low rating (1–2 stars) deducts 2 trust points
            await _trustService.DeductPointsAsync(dto.RateeId, 2);
        }

        // Keep driver average rating current when the ratee is a driver
        if (ratee.Role == UserRole.Driver)
        {
            var profile = await _userRepository.GetDriverProfileByUserIdAsync(ratee.Id);
            if (profile is not null)
                await _trustService.RecalculateDriverRatingAsync(profile.Id);
        }

        return ApiResponse<object>.SuccessMessage("Rating submitted. Thank you!");
    }

    /// <summary>
    /// Ratings are allowed only when there is a completed, cash-paid booking linking the parties on this ride.
    /// </summary>
    private async Task<bool> CanRateAsync(Ride ride, Guid raterId, Guid rateeId)
    {
        var driverProfile = ride.Driver
            ?? await _userRepository.GetDriverProfileByIdAsync(ride.DriverId);

        if (driverProfile is null)
            return false;

        var bookings = ride.Bookings?.ToList()
            ?? await _bookingRepository.GetBookingsByRideIdAsync(ride.Id);

        // Passenger rates driver after cash paid
        if (raterId != driverProfile.UserId && rateeId == driverProfile.UserId)
        {
            return bookings.Any(b =>
                b.PassengerId == raterId &&
                b.Status == BookingStatus.Completed &&
                b.IsCashPaid);
        }

        // Driver rates passenger after cash paid
        if (raterId == driverProfile.UserId && rateeId != driverProfile.UserId)
        {
            return bookings.Any(b =>
                b.PassengerId == rateeId &&
                b.Status == BookingStatus.Completed &&
                b.IsCashPaid);
        }

        return false;
    }
}
