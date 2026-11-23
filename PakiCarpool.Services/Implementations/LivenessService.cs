using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Driver;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.Services.Implementations;

/// <summary>
/// Issues server-selected liveness challenges and verifies the returned face measurements.
/// A nonce is short-lived, bound to one driver and an ordered prompt sequence, and consumed once.
/// </summary>
public class LivenessService : ILivenessService
{
    private const int NonceTtlDefaultMinutes = 2;
    private const int NonceBytes = 32;
    private const int ChallengePromptCount = 3;
    private const int MaxFramesPerPrompt = 600;
    private const int MaxSelfieBytes = 5 * 1024 * 1024;
    private const int MaxSelfieBase64Characters = 7_000_000;

    private static readonly string[] SupportedPrompts = ["Blink", "TurnLeft", "TurnRight", "Smile"];

    private readonly IUserRepository _userRepository;
    private readonly ILivenessChallengeRepository _challengeRepository;
    private readonly IStorageService _storageService;
    private readonly INotificationService _notificationService;
    private readonly IUserSessionService _userSessionService;
    private readonly IConfiguration _configuration;
    private readonly IHubContext<Hub> _hubContext;

    public LivenessService(
        IUserRepository userRepository,
        ILivenessChallengeRepository challengeRepository,
        IStorageService storageService,
        INotificationService notificationService,
        IUserSessionService userSessionService,
        IConfiguration configuration,
        IHubContext<Hub> hubContext)
    {
        _userRepository = userRepository;
        _challengeRepository = challengeRepository;
        _storageService = storageService;
        _notificationService = notificationService;
        _userSessionService = userSessionService;
        _configuration = configuration;
        _hubContext = hubContext;
    }

    /// <summary>
    /// Issues a cryptographically random nonce and an ordered, server-selected prompt sequence.
    /// Both are persisted with the configured expiry and must be used by the same driver.
    /// </summary>
    public async Task<LivenessChallengeDto> IssueChallengeAsync(Guid driverId, CancellationToken cancellationToken = default)
    {
        var ttlMinutes = _configuration.GetValue("Liveness:NonceTtlMinutes", NonceTtlDefaultMinutes);
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(NonceBytes))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var promptPool = (string[])SupportedPrompts.Clone();
        RandomNumberGenerator.Shuffle(promptPool);
        var prompts = promptPool.Take(ChallengePromptCount).ToArray();
        var now = DateTime.UtcNow;
        var challenge = new LivenessChallenge
        {
            DriverId = driverId,
            Nonce = nonce,
            PromptsJson = JsonSerializer.Serialize(prompts),
            IssuedAt = now,
            ExpiresAt = now.AddMinutes(ttlMinutes)
        };

        await _challengeRepository.InsertChallengeAsync(challenge, cancellationToken);

        return new LivenessChallengeDto
        {
            Nonce = nonce,
            Prompts = prompts,
            ExpiresAt = challenge.ExpiresAt
        };
    }

    /// <summary>
    /// Validates and consumes a driver-bound challenge, then evaluates the submitted measurements
    /// against the prompts stored for that challenge. Client-supplied pass/fail booleans are not used.
    /// </summary>
    public async Task<LivenessSubmissionResponseDto> ProcessLivenessResultAsync(
        Guid driverId,
        LivenessResultDto dto,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var challenge = await _challengeRepository.GetChallengeByNonceAsync(dto.Nonce, cancellationToken);
        if (challenge is null
            || challenge.DriverId != driverId
            || challenge.IsUsed
            || challenge.ExpiresAt <= now)
        {
            return await RejectSubmissionAsync(driverId, cancellationToken);
        }

        var challengeConsumed = await _challengeRepository.TryConsumeChallengeAsync(
            dto.Nonce,
            driverId,
            now,
            cancellationToken);
        if (!challengeConsumed)
            return await RejectSubmissionAsync(driverId, cancellationToken);

        var profile = await _userRepository.GetDriverProfileByUserIdAsync(driverId, cancellationToken);
        if (profile is null)
            throw new InvalidOperationException("Driver profile not found.");

        if (profile.LivenessBannedUntil.HasValue && profile.LivenessBannedUntil > DateTime.UtcNow)
        {
            return new LivenessSubmissionResponseDto
            {
                Success = false,
                Status = "Banned",
                Message = "Liveness submissions are temporarily unavailable.",
                RemainingAttempts = 0,
                BannedUntil = profile.LivenessBannedUntil
            };
        }

        var user = await _userRepository.GetByIdAsync(driverId, cancellationToken);
        if (user is null || !user.IsActive || user.IsSuspended)
        {
            return new LivenessSubmissionResponseDto
            {
                Status = "Rejected",
                Message = "Account is not active."
            };
        }

        if (!TryReadStoredPrompts(challenge.PromptsJson, out var expectedPrompts)
            || !IsServerVerifiedPass(expectedPrompts, dto.PromptResults))
        {
            return await RejectSubmissionAsync(driverId, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(dto.SelfieBase64) || dto.SelfieBase64.Length > MaxSelfieBase64Characters)
            return InvalidSelfieResponse();

        byte[] imageBytes;
        try
        {
            var base64Data = dto.SelfieBase64.Contains(',')
                ? dto.SelfieBase64[(dto.SelfieBase64.IndexOf(',') + 1)..]
                : dto.SelfieBase64;
            imageBytes = Convert.FromBase64String(base64Data);
        }
        catch (FormatException)
        {
            return InvalidSelfieResponse();
        }

        var contentType = GetImageContentType(imageBytes);
        if (imageBytes.Length is 0 or > MaxSelfieBytes || contentType is null)
            return InvalidSelfieResponse();

        string selfieUrl;
        try
        {
            selfieUrl = await _storageService.UploadDriverSelfieAsync(
                driverId,
                imageBytes,
                contentType,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to save selfie to storage.", ex);
        }

        profile.ProfilePhotoPath = selfieUrl;
        profile.PhotoUpdatedAt = DateTime.UtcNow;
        profile.LivenessBannedUntil = null;
        profile.LivenessVerified = true;
        profile.LivenessFailCount = 0;
        await _userRepository.UpdateDriverProfileAsync(profile, cancellationToken);

        var session = await _userSessionService.IssueAsync(user, cancellationToken);

        return new LivenessSubmissionResponseDto
        {
            Success = true,
            Status = "Approved",
            RemainingAttempts = AppConstants.MaxLivenessFailures,
            AccessToken = session.AccessToken,
            SessionIssuedAt = session.SessionIssuedAt,
            RawRefreshToken = session.RawRefreshToken,
            Message = "Server-verified liveness check approved."
        };
    }

    /// <summary>
    /// Returns historical submission state without issuing a session token. Every login requires
    /// a fresh server-verified challenge; historical verification never completes the current login.
    /// </summary>
    public async Task<LivenessSubmissionResponseDto> GetReviewStatusAsync(
        Guid driverId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _userRepository.GetDriverProfileByUserIdAsync(driverId, cancellationToken);
        if (profile is null)
            return new LivenessSubmissionResponseDto { Status = "NotSubmitted", Message = "Driver profile not found." };

        if (profile.LivenessBannedUntil > DateTime.UtcNow)
        {
            return new LivenessSubmissionResponseDto
            {
                Status = "Banned",
                BannedUntil = profile.LivenessBannedUntil,
                Message = "Liveness submissions are temporarily unavailable."
            };
        }

        if (profile.LivenessVerified)
        {
            return new LivenessSubmissionResponseDto
            {
                Status = "PreviouslyVerified",
                Message = "A fresh liveness challenge is required for this login."
            };
        }

        var isPending = !string.IsNullOrWhiteSpace(profile.ProfilePhotoPath) && profile.PhotoUpdatedAt.HasValue;
        return new LivenessSubmissionResponseDto
        {
            PendingReview = isPending,
            Status = isPending ? "PendingReview" : "NotSubmitted",
            Message = isPending
                ? "A previous selfie is awaiting administrator rejection or cleanup. Complete a fresh challenge to sign in."
                : "Complete a fresh liveness challenge to sign in."
        };
    }

    private static bool TryReadStoredPrompts(string promptsJson, out string[] prompts)
    {
        prompts = [];
        try
        {
            var storedPrompts = JsonSerializer.Deserialize<string[]>(promptsJson);
            if (storedPrompts is null
                || storedPrompts.Length != ChallengePromptCount
                || storedPrompts.Distinct(StringComparer.Ordinal).Count() != ChallengePromptCount
                || storedPrompts.Any(prompt => !SupportedPrompts.Contains(prompt, StringComparer.Ordinal)))
            {
                return false;
            }

            prompts = storedPrompts;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsServerVerifiedPass(
        IReadOnlyList<string> expectedPrompts,
        IReadOnlyCollection<LivenessPromptResultDto>? submittedResults)
    {
        if (submittedResults is null || submittedResults.Count != expectedPrompts.Count)
            return false;

        var results = submittedResults.ToArray();
        for (var index = 0; index < expectedPrompts.Count; index++)
        {
            var result = results[index];
            if (result is null
                || !string.Equals(result.Prompt, expectedPrompts[index], StringComparison.OrdinalIgnoreCase)
                || result.Frames is not { Count: > 0 and <= MaxFramesPerPrompt }
                || !VerifyPrompt(expectedPrompts[index], result.Frames))
            {
                return false;
            }
        }

        return true;
    }

    private static bool VerifyPrompt(string prompt, IReadOnlyCollection<LivenessFrameDto> frames)
    {
        return prompt switch
        {
            "Blink" => VerifyBlink(frames),
            "TurnLeft" => HasTwoConsecutiveQualifyingFrames(frames, frame => frame.HeadEulerAngleY, -180, 180, yaw => yaw < -20),
            "TurnRight" => HasTwoConsecutiveQualifyingFrames(frames, frame => frame.HeadEulerAngleY, -180, 180, yaw => yaw > 20),
            "Smile" => HasTwoConsecutiveQualifyingFrames(frames, frame => frame.SmilingProbability, 0, 1, probability => probability > 0.75),
            _ => false
        };
    }

    private static bool VerifyBlink(IEnumerable<LivenessFrameDto> frames)
    {
        var eyesWereClosed = false;
        var reopenedFrames = 0;
        foreach (var frame in frames)
        {
            var left = frame.LeftEyeOpenProbability;
            var right = frame.RightEyeOpenProbability;
            if (!IsProbability(left) || !IsProbability(right))
                return false;

            if (left < 0.3 && right < 0.3)
            {
                eyesWereClosed = true;
                reopenedFrames = 0;
                continue;
            }

            reopenedFrames = eyesWereClosed && left > 0.7 && right > 0.7 ? reopenedFrames + 1 : 0;
            if (reopenedFrames >= 2)
                return true;
        }

        return false;
    }

    private static bool HasTwoConsecutiveQualifyingFrames(
        IEnumerable<LivenessFrameDto> frames,
        Func<LivenessFrameDto, double?> measurement,
        double minimum,
        double maximum,
        Func<double, bool> qualifies)
    {
        var qualifyingFrames = 0;
        foreach (var frame in frames)
        {
            var value = measurement(frame);
            if (!value.HasValue || !double.IsFinite(value.Value) || value < minimum || value > maximum)
            {
                qualifyingFrames = 0;
                continue;
            }

            qualifyingFrames = qualifies(value.Value) ? qualifyingFrames + 1 : 0;
            if (qualifyingFrames >= 2)
                return true;
        }

        return false;
    }

    private static bool IsProbability(double? value)
        => value.HasValue && double.IsFinite(value.Value) && value is >= 0 and <= 1;

    private async Task<LivenessSubmissionResponseDto> RejectSubmissionAsync(
        Guid driverId,
        CancellationToken cancellationToken)
    {
        var response = await HandleFailureAsync(driverId, cancellationToken);
        response.Status = response.BannedUntil.HasValue && response.BannedUntil > DateTime.UtcNow
            ? "Banned"
            : "Rejected";
        response.Message = "The liveness challenge is invalid, expired, already used, or does not match its evidence.";
        return response;
    }

    private async Task<LivenessSubmissionResponseDto> HandleFailureAsync(Guid driverId, CancellationToken cancellationToken)
    {
        var profile = await _userRepository.GetDriverProfileByUserIdAsync(driverId, cancellationToken);
        if (profile is null)
            throw new InvalidOperationException("Driver profile not found.");

        profile.LivenessFailCount++;
        await _userRepository.UpdateDriverProfileAsync(profile, cancellationToken);

        if (profile.LivenessFailCount >= AppConstants.MaxLivenessFailures)
        {
            profile.LivenessBannedUntil = DateTime.UtcNow.AddHours(24);
            profile.LivenessFailCount = 0;
            await _userRepository.UpdateDriverProfileAsync(profile, cancellationToken);

            var driverUser = await _userRepository.GetByIdAsync(driverId, cancellationToken);
            var driverName = driverUser?.FullName ?? "Unknown";
            await _hubContext.Clients.Group("admins").SendAsync("LivenessFailureAlert", new
            {
                driverId,
                driverName,
                failCount = AppConstants.MaxLivenessFailures,
                timestamp = DateTime.UtcNow
            }, cancellationToken);

            await _notificationService.SendInAppNotificationAsync(
                driverId,
                NotificationType.LivenessFailure,
                "Liveness Check Flagged",
                $"You have failed liveness {AppConstants.MaxLivenessFailures} times. Account flagged for review.",
                cancellationToken);

            return new LivenessSubmissionResponseDto
            {
                Success = false,
                RemainingAttempts = 0,
                BannedUntil = profile.LivenessBannedUntil
            };
        }

        var remaining = Math.Max(0, AppConstants.MaxLivenessFailures - profile.LivenessFailCount);
        return new LivenessSubmissionResponseDto
        {
            Success = false,
            RemainingAttempts = remaining
        };
    }

    private static LivenessSubmissionResponseDto InvalidSelfieResponse()
    {
        return new LivenessSubmissionResponseDto
        {
            Status = "Rejected",
            Message = "A valid JPEG or PNG selfie under 5 MB is required."
        };
    }

    private static string? GetImageContentType(byte[] imageBytes)
    {
        if (imageBytes.Length >= 3 && imageBytes[0] == 0xFF && imageBytes[1] == 0xD8 && imageBytes[2] == 0xFF)
            return "image/jpeg";

        ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        return imageBytes.AsSpan().StartsWith(pngSignature) ? "image/png" : null;
    }
}
