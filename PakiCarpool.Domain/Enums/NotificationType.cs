namespace PakiCarpool.Domain.Enums;

public enum NotificationType
{
    EmailOtp,
    CarApproved,
    CarRejected,
    BookingRequest,
    BookingAccepted,
    BookingRejected,
    BookingAutoCancelled,
    DriverArrivedAtStop,
    RideEnded,
    RideCancelledByDriver,
    NoShowRecorded,
    WarningIssued,
    AccountSuspended,
    PasswordReset,
    NewDriverRegistration,
    LivenessFailure,
    CashReceived
}
