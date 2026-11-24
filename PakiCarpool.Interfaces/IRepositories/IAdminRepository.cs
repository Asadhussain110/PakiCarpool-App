using PakiCarpool.Domain.Models;

namespace PakiCarpool.Interfaces.IRepositories;

/// <summary>
/// Repository for admin-specific query operations.
/// Covers flag management and rating aggregations.
/// </summary>
public interface IAdminRepository
{
    /// <summary>
    /// Gets a flag by ID.
    /// </summary>
    Task<Flag?> GetFlagByIdAsync(Guid flagId);

    /// <summary>
    /// Gets all flags with pagination.
    /// </summary>
    Task<(List<Flag> Items, int TotalCount)> GetFlagsPaginatedAsync(int page, int pageSize);

    /// <summary>
    /// Gets the count of open flags.
    /// </summary>
    Task<int> CountOpenFlagsAsync();

    /// <summary>
    /// Gets all open flags raised against a user (as reportee).
    /// Used to keep threshold-raised admin-review flags idempotent.
    /// </summary>
    Task<List<Flag>> GetOpenFlagsByReporteeIdAsync(Guid reporteeId);

    /// <summary>
    /// Creates a new flag.
    /// </summary>
    Task<Flag> CreateFlagAsync(Flag flag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing flag.
    /// </summary>
    Task UpdateFlagAsync(Flag flag);

    /// <summary>
    /// Creates a new rating.
    /// </summary>
    Task<Rating> CreateRatingAsync(Rating rating);

    /// <summary>
    /// Gets the average rating for a user (as ratee).
    /// </summary>
    Task<decimal> GetAverageRatingAsync(Guid rateeId);

    /// <summary>
    /// Gets all ratings for a user (as ratee).
    /// </summary>
    Task<List<Rating>> GetRatingsForUserAsync(Guid rateeId);

    /// <summary>
    /// Checks if a rating already exists for a rater-ratee pair on a specific ride.
    /// </summary>
    Task<bool> RatingExistsAsync(Guid rideId, Guid raterId, Guid rateeId);
}
