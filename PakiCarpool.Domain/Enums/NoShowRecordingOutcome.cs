namespace PakiCarpool.Domain.Enums;

/// <summary>
/// Outcome of an atomic no-show recording attempt.
/// One no-show per booking is enforced under the ride lock and by the
/// unique index on NoShowLogs.BookingId, so retries and concurrent reports
/// never produce a second penalty.
/// </summary>
public enum NoShowRecordingOutcome
{
    /// <summary>The booking could not be found.</summary>
    NotFound,

    /// <summary>The booking is no longer in an eligible state for a no-show report.</summary>
    BookingNotEligible,

    /// <summary>A no-show has already been recorded for this booking; nothing was changed.</summary>
    AlreadyReported,

    /// <summary>The no-show was recorded: log persisted, offender penalized, booking cancelled.</summary>
    Recorded
}
