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
    /// Processes liveness verification result. On pass: saves selfie, sets LivenessVerified = true,
    /// resets fail count, and returns full access token replacing the LivenessPending token.
    /// </summary>
    Task<ApiResponse<object>> ProcessLivenessResultAsync(Guid driverId, LivenessResultDto dto);

    /// <summary>
    /// Reports consecutive liveness failures. After 5 failures: flags account and notifies admin.
    /// Returns bannedUntil timestamp if threshold is reached.
    /// </summary>
    Task<ApiResponse<object>> ReportLivenessFailureAsync(Guid driverId);
}
