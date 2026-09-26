using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.DTOs.Driver;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Helpers;

namespace PakiCarpool.Services.Implementations;

/// <summary>
/// Handles driver liveness verification results.
/// Liveness check is required at EVERY driver login, not just registration.
/// On pass: saves selfie, sets LivenessVerified = true, resets fail count, issues full JWT.
/// After 5 consecutive failures: flags account and notifies admin.
/// </summary>
public class LivenessService : ILivenessService
{
    private readonly IUserRepository _userRepository;
    private readonly INotificationService _notificationService;
    private readonly JwtHelper _jwtHelper;
    private readonly IConfiguration _configuration;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<Microsoft.AspNetCore.SignalR.Hub> _hubContext;

    public LivenessService(
        IUserRepository userRepository,
        INotificationService notificationService,
        JwtHelper jwtHelper,
        IConfiguration configuration,
        Microsoft.AspNetCore.SignalR.IHubContext<Microsoft.AspNetCore.SignalR.Hub> hubContext)
    {
        _userRepository = userRepository;
        _notificationService = notificationService;
        _jwtHelper = jwtHelper;
        _configuration = configuration;
        _hubContext = hubContext;
    }

    /// <summary>
    /// Processes the liveness result submitted by the Flutter app.
    /// On pass: saves selfie to disk, sets LivenessVerified = true, resets fail count,
    /// and returns a full access token replacing the LivenessPending token.
    /// On fail or invalid result: returns an error response.
    /// </summary>
    public async Task<ApiResponse<object>> ProcessLivenessResultAsync(Guid driverId, LivenessResultDto dto)
    {
        var isSuccessResult = string.Equals(dto.LivenessResult, "SUCCESS", StringComparison.OrdinalIgnoreCase);
        if (!isSuccessResult && !string.Equals(dto.LivenessResult, "pass", StringComparison.OrdinalIgnoreCase))
        {
            return new ApiResponse<object>
            {
                Message = "Liveness check did not pass. Please try again."
            };
        }

        var profile = await _userRepository.GetDriverProfileByUserIdAsync(driverId);
        if (profile is null)
        {
            return new ApiResponse<object>
            {
                Message = "Driver profile not found."
            };
        }

        // Check for liveness ban
        if (profile.LivenessBannedUntil.HasValue && profile.LivenessBannedUntil > DateTime.UtcNow)
        {
            return new ApiResponse<object>
            {
                Data = new { bannedUntil = profile.LivenessBannedUntil },
                Message = ErrorCodes.LivenessBanned
            };
        }

        // Save selfie to storage
        var selfieRelativePath = await SaveSelfieAsync(driverId, dto.SelfieBase64);
        profile.ProfilePhotoPath = selfieRelativePath;
        profile.LivenessVerified = true;
        profile.LivenessFailCount = 0;
        profile.LivenessBannedUntil = null;
        await _userRepository.UpdateDriverProfileAsync(profile);

        // Issue a full access token (replaces the LivenessPending partial token)
        var user = await _userRepository.GetByIdAsync(driverId)
                   ?? throw new InvalidOperationException($"User {driverId} not found.");

        var fullToken = _jwtHelper.GenerateAccessToken(user.Id, user.Email, user.Role.ToString());

        var message = isSuccessResult ? "Liveness verified." : "Liveness verified successfully.";
        return ApiResponse<object>.Success(new { accessToken = fullToken }, message);
    }

    /// <summary>
    /// Increments the liveness fail count for a driver.
    /// After 5 consecutive failures: bans for 24 hours and notifies admin via in-app SignalR.
    /// </summary>
    public async Task<ApiResponse<object>> ReportLivenessFailureAsync(Guid driverId)
    {
        var profile = await _userRepository.GetDriverProfileByUserIdAsync(driverId);
        if (profile is null)
        {
            return new ApiResponse<object> { Message = "Driver profile not found." };
        }

        profile.LivenessFailCount++;
        await _userRepository.UpdateDriverProfileAsync(profile);

        if (profile.LivenessFailCount >= AppConstants.MaxLivenessFailures)
        {
            // Ban for 24 hours and notify admin
            profile.LivenessBannedUntil = DateTime.UtcNow.AddHours(24);
            profile.LivenessFailCount = 0; // Reset counter after ban
            await _userRepository.UpdateDriverProfileAsync(profile);

            // Fetch driver info for the notification
            var driverUser = await _userRepository.GetByIdAsync(driverId);
            var driverName = driverUser?.FullName ?? "Unknown";

            // Broadcast to all admins via SignalR admin group
            await _hubContext.Clients.Group("admins").SendAsync("LivenessFailureAlert", new
            {
                driverId = driverId,
                driverName = driverName,
                failCount = AppConstants.MaxLivenessFailures,
                timestamp = DateTime.UtcNow
            });

            // Keep the in-app notification for the driver
            await _notificationService.SendInAppNotificationAsync(
                driverId,
                NotificationType.LivenessFailure,
                "Liveness Check Flagged",
                $"You have failed liveness {AppConstants.MaxLivenessFailures} times. Account flagged — review required.");

            return ApiResponse<object>.Success(
                new { bannedUntil = profile.LivenessBannedUntil },
                "Account has been temporarily flagged due to repeated liveness failures.");
        }

        var remaining = AppConstants.MaxLivenessFailures - profile.LivenessFailCount;
        return ApiResponse<object>.Success(
            new { failCount = profile.LivenessFailCount, attemptsRemaining = remaining },
            $"Liveness check failed. {remaining} attempt(s) remaining before account flag.");
    }

    // ─── Private Helpers ───────────────────────────────────────────────────────

    private async Task<string> SaveSelfieAsync(Guid driverId, string selfieBase64)
    {
        var storagePath = _configuration["FILE_STORAGE_PATH"] ?? "/var/pakicarpool/uploads";
        var selfieDir = Path.Combine(storagePath, "selfies");
        Directory.CreateDirectory(selfieDir);

        // Strip data URI prefix if present (e.g., "data:image/jpeg;base64,...")
        var base64Data = selfieBase64.Contains(',')
            ? selfieBase64.Split(',')[1]
            : selfieBase64;

        var imageBytes = Convert.FromBase64String(base64Data);
        var fileName = $"{driverId}_{DateTime.UtcNow:yyyyMMddHHmmss}.jpg";
        var fullPath = Path.Combine(selfieDir, fileName);

        await File.WriteAllBytesAsync(fullPath, imageBytes);

        return Path.Combine("selfies", fileName); // Return relative path for storage
    }
}
