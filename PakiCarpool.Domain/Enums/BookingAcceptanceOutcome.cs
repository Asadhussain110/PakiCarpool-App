namespace PakiCarpool.Domain.Enums;

/// <summary>
/// Describes the authoritative outcome of an atomic booking-acceptance attempt.
/// </summary>
public enum BookingAcceptanceOutcome
{
    Accepted,
    NotFound,
    RideNotFound,
    StopsNotFound,
    RideCancelled,
    RideCompleted,
    AlreadyAccepted,
    Rejected,
    Cancelled,
    NotPending,
    SegmentFull,
    TimedOut
}
