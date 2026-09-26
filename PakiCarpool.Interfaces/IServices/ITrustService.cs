using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.Enums;

namespace PakiCarpool.Interfaces.IServices;

/// <summary>
/// Defines trust score management operations.
/// Handles cancellation classification, point deduction/addition, and threshold-based actions.
/// </summary>
public interface ITrustService
{
    /// <summary>
    /// Classifies a cancellation reason as Valid or Frivolous.
    /// Frivolous keywords: "don't want", "changed mind", "just cancel", "bored", "nvm", "never mind", "test".
    /// Under 20 chars with no substance = Frivolous.
    /// </summary>
    CancellationClassification ClassifyCancellation(string reason);

    /// <summary>
    /// Deducts trust points from a user. Checks thresholds and triggers warnings or suspension.
    /// </summary>
    Task DeductPointsAsync(Guid userId, int points);

    /// <summary>
    /// Adds trust points to a user (e.g., positive rating +3, ride completion +1).
    /// </summary>
    Task AddPointsAsync(Guid userId, int points);

    /// <summary>
    /// Recalculates average rating for a driver from the Ratings table.
    /// Called by TrustScoreJob daily.
    /// </summary>
    Task RecalculateDriverRatingAsync(Guid driverId);

    /// <summary>
    /// Checks all trust-related thresholds for a user and triggers appropriate actions.
    /// Frivolous cancellations >= 3: warning. >= 5: suspend.
    /// No-shows >= 2: warning. >= 4: suspend.
    /// Trust score < 50: warning. < 30: suspend.
    /// </summary>
    Task CheckThresholdsAsync(Guid userId);
}
