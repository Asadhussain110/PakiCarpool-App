using PakiCarpool.Domain.DTOs.Common;

namespace PakiCarpool.Interfaces.IServices;

/// <summary>
/// Defines rating operations for both drivers and passengers.
/// Ratings are submitted post-ride after cash payment is confirmed.
/// </summary>
public interface IRatingService
{
    /// <summary>
    /// Submits a rating for a ride participant. Stars 1-5, optional review text (max 300 chars).
    /// Positive ratings (4-5 stars) add +3 trust points to the ratee.
    /// </summary>
    Task<ApiResponse<object>> SubmitRatingAsync(Guid raterId, RatingDto dto);
}
