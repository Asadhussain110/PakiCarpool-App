using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.Services.Implementations;

/// <summary>
/// Manages trust scores, cancellation classification, and threshold-based account actions.
/// Initial score: 100. Deductions for no-shows and frivolous cancellations.
/// Bonuses for positive ratings and ride completions.
/// </summary>
public class TrustService : ITrustService
{
    private readonly IUserRepository _userRepository;
    private readonly IAdminRepository _adminRepository;
    private readonly INotificationService _notificationService;
    private readonly IEmailService _emailService;

    // Frivolous keyword list — lowercase for case-insensitive matching
    private static readonly string[] FrivolousKeywords =
    [
        "don't want", "dont want", "changed mind", "just cancel",
        "bored", "nvm", "never mind", "test"
    ];

    public TrustService(
        IUserRepository userRepository,
        IAdminRepository adminRepository,
        INotificationService notificationService,
        IEmailService emailService)
    {
        _userRepository = userRepository;
        _adminRepository = adminRepository;
        _notificationService = notificationService;
        _emailService = emailService;
    }

    /// <summary>
    /// Classifies a cancellation reason as Valid or Frivolous.
    /// Frivolous: contains any frivolous keyword, OR under 20 chars with no substance.
    /// </summary>
    public CancellationClassification ClassifyCancellation(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return CancellationClassification.Frivolous;

        var lower = reason.Trim().ToLowerInvariant();

        foreach (var keyword in FrivolousKeywords)
        {
            if (lower.Contains(keyword))
                return CancellationClassification.Frivolous;
        }

        // Under 20 non-whitespace characters = no substance
        var substantiveChars = reason.Count(c => !char.IsWhiteSpace(c));
        if (substantiveChars < 20)
            return CancellationClassification.Frivolous;

        return CancellationClassification.Valid;
    }

    /// <summary>
    /// Deducts trust points from a user. After deduction, checks thresholds and
    /// triggers warnings or suspension as required.
    /// </summary>
    public async Task DeductPointsAsync(Guid userId, int points)
    {
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new InvalidOperationException($"User {userId} not found.");

        user.TrustScore = Math.Max(0, user.TrustScore - points);
        await _userRepository.UpdateAsync(user);
        await CheckThresholdsAsync(userId);
    }

    /// <summary>
    /// Adds trust points to a user (capped at initial value of 100).
    /// </summary>
    public async Task AddPointsAsync(Guid userId, int points)
    {
        var user = await _userRepository.GetByIdAsync(userId)
            ?? throw new InvalidOperationException($"User {userId} not found.");

        user.TrustScore = Math.Min(AppConstants.TrustScoreInitial, user.TrustScore + points);
        await _userRepository.UpdateAsync(user);
    }

    /// <summary>
    /// Recalculates the AverageRating for a driver from the Ratings table.
    /// Called daily by TrustScoreJob and after each new rating submission.
    /// </summary>
    public async Task RecalculateDriverRatingAsync(Guid driverId)
    {
        var profile = await _userRepository.GetDriverProfileByIdAsync(driverId);
        if (profile is null) return;

        var average = await _adminRepository.GetAverageRatingAsync(profile.UserId);
        profile.AverageRating = average;
        await _userRepository.UpdateDriverProfileAsync(profile);
    }

    /// <summary>
    /// Checks all trust-related thresholds for a user and triggers appropriate actions.
    /// Runs after every deduction to ensure thresholds are never missed.
    /// </summary>
    public async Task CheckThresholdsAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user is null) return;

        var frivolousCount = await _userRepository.CountFrivolousCancellationsAsync(userId);
        var noShowCount = await _userRepository.CountNoShowsAsync(userId);

        // ── Frivolous cancellation thresholds ─────────────────────────────────
        if (frivolousCount >= AppConstants.FrivolousCancellationSuspendThreshold)
        {
            if (!user.IsSuspended)
            {
                user.IsSuspended = true;
                user.IsActive = false;
                await _userRepository.UpdateAsync(user);

                var reason = "Repeated frivolous cancellations. Your account has been suspended.";
                await _notificationService.SendInAppNotificationAsync(
                    userId, NotificationType.AccountSuspended,
                    "Account Suspended", reason);
                await _emailService.SendSuspensionEmailAsync(user.Email, user.FullName, reason);
            }
        }
        else if (frivolousCount >= AppConstants.FrivolousCancellationWarningThreshold)
        {
            var reason = $"You have {frivolousCount} frivolous cancellations. At 5, your account will be suspended.";
            await _notificationService.SendInAppNotificationAsync(
                userId, NotificationType.WarningIssued,
                "Cancellation Warning", reason);
            await _emailService.SendWarningEmailAsync(user.Email, user.FullName, reason);
        }

        // ── No-show thresholds ─────────────────────────────────────────────────
        // Suspension for repeated no-shows is an admin decision by design:
        // at the threshold an open flag is raised once for admin review — never an automatic suspension.
        if (noShowCount >= AppConstants.NoShowAdminReviewThreshold)
        {
            var openFlags = await _adminRepository.GetOpenFlagsByReporteeIdAsync(userId);
            if (!openFlags.Any(f => f.Reason == AppConstants.NoShowReviewFlagReason))
            {
                var latestNoShow = await _userRepository.GetLatestNoShowByOffenderAsync(userId);
                if (latestNoShow is not null)
                {
                    await _adminRepository.CreateFlagAsync(new Flag
                    {
                        Id = Guid.NewGuid(),
                        ReporterId = latestNoShow.ReporterId,
                        ReporteeId = userId,
                        RideId = latestNoShow.RideId,
                        Reason = AppConstants.NoShowReviewFlagReason,
                        Status = FlagStatus.Open,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
        }
        else if (noShowCount >= AppConstants.NoShowWarningThreshold)
        {
            var reason = $"You have {noShowCount} no-shows recorded. At {AppConstants.NoShowAdminReviewThreshold}, your account will be flagged for admin review of suspension.";
            await _notificationService.SendInAppNotificationAsync(
                userId, NotificationType.WarningIssued,
                "No-Show Warning", reason);
            await _emailService.SendWarningEmailAsync(user.Email, user.FullName, reason);
        }

        // ── Trust score thresholds ─────────────────────────────────────────────
        if (user.TrustScore < AppConstants.TrustScoreSuspendThreshold)
        {
            if (!user.IsSuspended)
            {
                user.IsSuspended = true;
                user.IsActive = false;
                await _userRepository.UpdateAsync(user);

                var reason = $"Your trust score has dropped below {AppConstants.TrustScoreSuspendThreshold}. Your account has been suspended.";
                await _notificationService.SendInAppNotificationAsync(
                    userId, NotificationType.AccountSuspended,
                    "Account Suspended", reason);
                await _emailService.SendSuspensionEmailAsync(user.Email, user.FullName, reason);
            }
        }
        else if (user.TrustScore < AppConstants.TrustScoreWarningThreshold)
        {
            var reason = $"Your trust score is {user.TrustScore}. Maintain good behaviour to avoid suspension.";
            await _notificationService.SendInAppNotificationAsync(
                userId, NotificationType.WarningIssued,
                "Low Trust Score Warning", reason);
            await _emailService.SendWarningEmailAsync(user.Email, user.FullName, reason);
        }
    }
}
