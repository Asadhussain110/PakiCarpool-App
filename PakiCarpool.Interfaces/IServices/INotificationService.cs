using PakiCarpool.Domain.Enums;

namespace PakiCarpool.Interfaces.IServices;

/// <summary>
/// Defines notification delivery operations across three channels:
/// in-app (SignalR), email (Brevo SMTP), and phone (in-app push simulation).
/// Structured to allow FCM plug-in later without changing callers.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Sends an in-app notification via SignalR to the user's personal group.
    /// </summary>
    Task SendInAppNotificationAsync(Guid userId, NotificationType type, string title, string message);

    /// <summary>
    /// Sends an in-app notification to a group of users.
    /// </summary>
    Task SendInAppNotificationToGroupAsync(IEnumerable<Guid> userIds, NotificationType type, string title, string message);

    /// <summary>
    /// Sends a notification through all appropriate channels based on the notification matrix.
    /// Determines which channels to use based on the NotificationType.
    /// </summary>
    Task SendNotificationAsync(Guid userId, NotificationType type, string title, string message, object? additionalData = null);

    /// <summary>
    /// Sends a phone push simulation notification via SignalR.
    /// Placeholder for future FCM integration.
    /// </summary>
    Task SendPushNotificationAsync(Guid userId, string title, string message);

    // ─────────────────────────────────────────────────────────────────────────
    // Named event broadcasts (server → Flutter client)
    // Each method maps 1-to-1 to a specific SignalR event name and payload.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Notifies a passenger that their booking was accepted (user-{passengerId}).</summary>
    Task SendBookingAcceptedAsync(Guid passengerId, Guid bookingId, string driverName, string carModel);

    /// <summary>Notifies a passenger that their booking was rejected (user-{passengerId}).</summary>
    Task SendBookingRejectedAsync(Guid passengerId, Guid bookingId, string reason);

    /// <summary>Notifies a passenger that their booking was auto-cancelled (user-{passengerId}).</summary>
    Task SendBookingAutoCancelledAsync(Guid passengerId, Guid bookingId);

    /// <summary>
    /// Notifies all passengers in a ride group that the driver arrived at a stop (ride-{rideId}).
    /// </summary>
    Task SendDriverArrivedAtStopAsync(Guid rideId, string stopName, Guid stopId);

    /// <summary>Notifies a passenger that their ride segment has ended (user-{passengerId}).</summary>
    Task SendRideEndedForPassengerAsync(Guid passengerId, Guid bookingId, decimal fare);

    /// <summary>Notifies a passenger that cash payment was confirmed (user-{passengerId}).</summary>
    Task SendCashReceivedConfirmedAsync(Guid passengerId, Guid bookingId);

    /// <summary>
    /// Notifies all passengers in a ride group that the driver cancelled the ride (ride-{rideId}).
    /// </summary>
    Task SendRideCancelledByDriverAsync(Guid rideId, string reason);

    /// <summary>
    /// Notifies an auto-rejected pending passenger that the ride is now full (user-{passengerId}).
    /// </summary>
    Task SendRideFullNotificationAsync(Guid passengerId, Guid rideId);
}
