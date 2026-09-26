namespace PakiCarpool.Common.Extensions;

/// <summary>
/// DateTime utility extensions for token expiry checks and timestamp conversions.
/// </summary>
public static class DateTimeExtensions
{
    /// <summary>
    /// Returns true if the DateTime is in the past (expired).
    /// </summary>
    public static bool IsExpired(this DateTime dateTime)
    {
        return dateTime < DateTime.UtcNow;
    }

    /// <summary>
    /// Returns true if the nullable DateTime is in the future (still locked).
    /// </summary>
    public static bool IsLockedOut(this DateTime? lockoutUntil)
    {
        return lockoutUntil.HasValue && lockoutUntil.Value > DateTime.UtcNow;
    }

    /// <summary>
    /// Converts a DateTime to Unix timestamp (seconds since epoch).
    /// </summary>
    public static long ToUnixTimestamp(this DateTime dateTime)
    {
        return new DateTimeOffset(dateTime, TimeSpan.Zero).ToUnixTimeSeconds();
    }

    /// <summary>
    /// Converts a Unix timestamp to DateTime (UTC).
    /// </summary>
    public static DateTime FromUnixTimestamp(long timestamp)
    {
        return DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
    }

    /// <summary>
    /// Returns the remaining time until the specified DateTime, or TimeSpan.Zero if already passed.
    /// </summary>
    public static TimeSpan RemainingTime(this DateTime dateTime)
    {
        var remaining = dateTime - DateTime.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
