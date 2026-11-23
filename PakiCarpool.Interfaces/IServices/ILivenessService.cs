using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.DTOs.Driver;

namespace PakiCarpool.Interfaces.IServices;

/// <summary>
/// Defines liveness verification operations for drivers.
/// Liveness check is required at every driver login, not just registration.
/// </summary>
public interface ILivenessService
{
    /// <summary>
    /// Issues a short-lived, one-time challenge nonce and ordered server-selected prompts.
    /// The nonce, driver, prompts, and expiry are persisted together; the nonce is consumed
    /// immediately after processing, whether the check passes or fails.
    /// TTL is controlled by <c>Liveness:NonceTtlMinutes</c> in configuration.
    /// </summary>
    Task<LivenessChallengeDto> IssueChallengeAsync(Guid driverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes face measurements against the server-stored prompt sequence. On pass: saves selfie,
    /// sets the historical LivenessVerified flag, resets fail count, and returns a full access token,
    /// its signed session timestamp, and a refresh token for the controller's HTTP-only cookie.
    /// </summary>
    Task<LivenessSubmissionResponseDto> ProcessLivenessResultAsync(
        Guid driverId,
        LivenessResultDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns historical submission state without issuing a token or completing the current login.
    /// </summary>
    Task<LivenessSubmissionResponseDto> GetReviewStatusAsync(Guid driverId, CancellationToken cancellationToken = default);
}
