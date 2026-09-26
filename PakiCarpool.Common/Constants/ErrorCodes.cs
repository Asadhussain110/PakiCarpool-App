namespace PakiCarpool.Common.Constants;

/// <summary>
/// All standardized error codes returned by the API.
/// Never return raw strings — always use these constants.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string StopAlreadyPassed = "STOP_ALREADY_PASSED";
    public const string StopSegmentFull = "STOP_SEGMENT_FULL";
    public const string ActiveBookingExists = "ACTIVE_BOOKING_EXISTS";
    public const string ActiveRideExists = "ACTIVE_RIDE_EXISTS";
    public const string CarNotApproved = "CAR_NOT_APPROVED";
    public const string LivenessRequired = "LIVENESS_REQUIRED";
    public const string LivenessFailed = "LIVENESS_FAILED";
    public const string LivenessBanned = "LIVENESS_BANNED";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string TokenExpired = "TOKEN_EXPIRED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string BookingTimeout = "BOOKING_TIMEOUT";
    public const string AccountLocked = "ACCOUNT_LOCKED";
    public const string AccountSuspended = "ACCOUNT_SUSPENDED";
    public const string RateLimitExceeded = "RATE_LIMIT_EXCEEDED";
    public const string InternalError = "INTERNAL_ERROR";
    public const string CaptchaRequired = "CAPTCHA_REQUIRED";
    public const string CaptchaInvalid = "CAPTCHA_INVALID";
}
