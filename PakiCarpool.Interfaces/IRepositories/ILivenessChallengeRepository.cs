using PakiCarpool.Domain.Models;

namespace PakiCarpool.Interfaces.IRepositories;

/// <summary>
/// Repository for <see cref="LivenessChallenge"/> persistence operations.
/// Kept separate from IUserRepository so the liveness subsystem is self-contained.
/// </summary>
public interface ILivenessChallengeRepository
{
    /// <summary>
    /// Inserts a new <see cref="LivenessChallenge"/> into the database.
    /// </summary>
    Task InsertChallengeAsync(LivenessChallenge challenge, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a <see cref="LivenessChallenge"/> by its nonce value.
    /// Returns <c>null</c> if no matching challenge exists.
    /// </summary>
    Task<LivenessChallenge?> GetChallengeByNonceAsync(
        string nonce,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a challenge as used so it cannot be replayed.
    /// Must be called regardless of whether the liveness check passed or failed.
    /// </summary>
    Task MarkChallengeUsedAsync(LivenessChallenge challenge);

    Task<bool> TryConsumeChallengeAsync(
        string nonce,
        Guid driverId,
        DateTime now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all challenges that are expired (<c>ExpiresAt &lt; now</c>) or already used.
    /// Called by <c>LivenessChallengeCleanupJob</c>.
    /// </summary>
    Task CleanupChallengesAsync();
}
