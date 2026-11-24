namespace PakiCarpool.Common.Constants;

/// <summary>
/// Application-wide constants derived from the Paki Carpool specification.
/// No magic numbers — all thresholds and limits are centralized here.
/// </summary>
public static class AppConstants
{
    // Trust Score
    public const int TrustScoreInitial = 100;
    public const int FrivolousCancellationPenalty = 5;
    public const int NoShowPenalty = 10;
    public const int PositiveRatingBonus = 3;
    public const int RideCompletionBonus = 1;

    // Trust Score Thresholds
    public const int FrivolousCancellationWarningThreshold = 3;
    public const int FrivolousCancellationSuspendThreshold = 5;
    public const int NoShowWarningThreshold = 2;
    public const int NoShowAdminReviewThreshold = 4;
    public const int TrustScoreWarningThreshold = 50;
    public const int TrustScoreSuspendThreshold = 30;

    // Ride
    public const int CancelGracePeriodMinutes = 5;
    public const int MinStopsPerRide = 2;
    public const int MaxAvailableSeats = 6;

    // Booking
    public const int BookingTimeoutMinutes = 2;
    public const string BookingTimeoutCancelReason = "Auto-cancelled: driver did not respond within 2 minutes";
    public const int MinCancelReasonLength = 10;
    public const int NoShowWaitMinutes = 3;
    public const int NoShowGpsFreshnessMinutes = 3;
    public const string NoShowPassengerCancelReason = "Cancelled: passenger no-show at pickup stop";
    public const string NoShowDriverCancelReason = "Cancelled: driver no-show at pickup";
    public const string NoShowReviewFlagReason = "Repeated no-shows: admin review required for suspension";

    // Authentication
    public const int MaxLoginAttempts = 5;
    public const int CaptchaRequiredAfterAttempts = 3;
    public const int LockoutDurationMinutes = 15;
    public const int OtpLength = 6;
    public const int BcryptWorkFactor = 12;

    // Liveness
    public const int MaxLivenessFailures = 5;
    public const int LivenessTimeoutSeconds = 10;

    // Chat
    public const int ChatExpiryDays = 7;
    public const int MaxChatMessageLength = 1000;

    // Rating
    public const int MaxReviewTextLength = 300;
    public const int MinRatingStars = 1;
    public const int MaxRatingStars = 5;
    public const int PositiveRatingMinStars = 4;

    // Rate Limiting
    public const int RateLimitGeneralPerMinute = 100;
    public const int RateLimitAuthPerMinute = 10;

    // File Upload
    public const long MaxFileUploadSizeBytes = 10 * 1024 * 1024; // 10 MB
}
