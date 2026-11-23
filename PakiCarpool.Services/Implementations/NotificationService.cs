using Microsoft.AspNetCore.SignalR;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.Services.Implementations;

/// <summary>
/// Delivers notifications across three channels: in-app (SignalR), email (IEmailService),
/// and phone push (SignalR simulation — FCM plug-in point).
/// Callers are channel-agnostic — this service decides which channels apply per NotificationType.
/// </summary>
public class NotificationService : INotificationService
{
    // IHubContext is injected as a non-generic base type to avoid a circular project reference.
    // The concrete hub type (PakiCarpoolHub) lives in the API project, so we use the
    // dynamic dispatch pattern via HubClients.Group/User string API.
    private readonly IHubContext<Microsoft.AspNetCore.SignalR.Hub> _hubContext;
    private readonly IEmailService _emailService;
    private readonly IBookingRepository _bookingRepository;
    private readonly IRideGroupConnectionTracker _rideGroupConnectionTracker;

    public NotificationService(
        IHubContext<Microsoft.AspNetCore.SignalR.Hub> hubContext,
        IEmailService emailService,
        IBookingRepository bookingRepository,
        IRideGroupConnectionTracker rideGroupConnectionTracker)
    {
        _hubContext = hubContext;
        _emailService = emailService;
        _bookingRepository = bookingRepository;
        _rideGroupConnectionTracker = rideGroupConnectionTracker;
    }

    /// <summary>
    /// Sends an in-app notification via SignalR to the user's personal group (user-{userId}).
    /// </summary>
    public async Task SendInAppNotificationAsync(
        Guid userId,
        NotificationType type,
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients
            .Group($"user-{userId}")
            .SendAsync("ReceiveNotification", new
            {
                type = type.ToString(),
                title,
                message,
                timestamp = DateTime.UtcNow
            }, cancellationToken);
    }

    /// <summary>
    /// Sends an in-app notification to a group of users simultaneously.
    /// </summary>
    public async Task SendInAppNotificationToGroupAsync(
        IEnumerable<Guid> userIds,
        NotificationType type,
        string title,
        string message)
    {
        var tasks = userIds.Select(id => SendInAppNotificationAsync(id, type, title, message));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Routes a notification through all appropriate channels based on the notification matrix.
    /// In-app channel is always used when applicable; email is per-type.
    /// </summary>
    public async Task SendNotificationAsync(
        Guid userId,
        NotificationType type,
        string title,
        string message,
        object? additionalData = null)
    {
        // Always send in-app for types that support it
        if (IsInAppType(type))
        {
            await SendInAppNotificationAsync(userId, type, title, message);
        }

        // Phone push simulation (FCM stub)
        if (IsPhonePushType(type))
        {
            await SendPushNotificationAsync(userId, title, message);
        }
    }

    public async Task RemovePassengerFromRideGroupsAsync(
        Guid rideId,
        Guid passengerId,
        CancellationToken cancellationToken = default)
    {
        var connectionIds = await _rideGroupConnectionTracker.GetConnectionsAsync(rideId, passengerId, cancellationToken);
        foreach (var connectionId in connectionIds)
        {
            await _hubContext.Groups.RemoveFromGroupAsync(connectionId, $"ride-{rideId:D}", cancellationToken);
            await _hubContext.Groups.RemoveFromGroupAsync(connectionId, $"ride-{rideId:D}-pax-{passengerId:D}", cancellationToken);
        }

        await _rideGroupConnectionTracker.RemoveAsync(rideId, passengerId, cancellationToken);
    }

    private async Task RemoveBookingFromRideGroupsAsync(Guid bookingId)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId);
        if (booking is not null)
            await RemovePassengerFromRideGroupsAsync(booking.RideId, booking.PassengerId);
    }

    /// <summary>
    /// Sends a push notification simulation via SignalR.
    /// This is the FCM plug-in point — replace the SignalR call with FCM SDK when ready.
    /// </summary>
    public async Task SendPushNotificationAsync(Guid userId, string title, string message)
    {
        await _hubContext.Clients
            .Group($"user-{userId}")
            .SendAsync("ReceivePushNotification", new
            {
                title,
                message,
                timestamp = DateTime.UtcNow
            });
    }

    // ─── Named Event Broadcasts (server → Flutter) ────────────────────────────

    /// <inheritdoc />
    public Task SendBookingAcceptedAsync(Guid passengerId, Guid bookingId, string driverName, string carModel)
        => _hubContext.Clients
            .Group($"user-{passengerId}")
            .SendAsync("BookingAccepted", new
            {
                bookingId,
                driverName,
                carModel
            });

    /// <inheritdoc />
    public async Task SendBookingRejectedAsync(Guid passengerId, Guid bookingId, string reason)
    {
        await RemoveBookingFromRideGroupsAsync(bookingId);
        await _hubContext.Clients
            .Group($"user-{passengerId}")
            .SendAsync("BookingRejected", new
            {
                bookingId,
                reason
            });
    }

    /// <inheritdoc />
    public async Task SendBookingAutoCancelledAsync(Guid passengerId, Guid bookingId)
    {
        await RemoveBookingFromRideGroupsAsync(bookingId);
        await _hubContext.Clients
            .Group($"user-{passengerId}")
            .SendAsync("BookingAutoCancelled", new { bookingId });
    }

    /// <inheritdoc />
    public Task SendDriverArrivedAtStopAsync(Guid rideId, string stopName, Guid stopId)
        => _hubContext.Clients
            .Group($"ride-{rideId}")
            .SendAsync("DriverArrivedAtStop", new
            {
                stopName,
                stopId
            });

    /// <inheritdoc />
    public async Task SendRideEndedForPassengerAsync(Guid passengerId, Guid bookingId, decimal fare)
    {
        await RemoveBookingFromRideGroupsAsync(bookingId);
        await _hubContext.Clients
            .Group($"user-{passengerId}")
            .SendAsync("RideEndedForPassenger", new
            {
                bookingId,
                fare
            });
    }

    /// <inheritdoc />
    public Task SendCashReceivedConfirmedAsync(Guid passengerId, Guid bookingId)
        => _hubContext.Clients
            .Group($"user-{passengerId}")
            .SendAsync("CashReceivedConfirmed", new { bookingId });

    /// <inheritdoc />
    public Task SendRideCancelledByDriverAsync(Guid rideId, string reason)
        => _hubContext.Clients
            .Group($"ride-{rideId}")
            .SendAsync("RideCancelledByDriver", new
            {
                rideId,
                reason
            });

    /// <inheritdoc />
    public async Task SendRideFullNotificationAsync(Guid passengerId, Guid rideId)
    {
        await RemovePassengerFromRideGroupsAsync(rideId, passengerId);
        await _hubContext.Clients
            .Group($"user-{passengerId}")
            .SendAsync("RideFullNotification", new { rideId });
    }

    // ─── Private Helpers (Notification Matrix) ─────────────────────────────────

    /// <summary>
    /// Types that use the in-app SignalR channel per the notification matrix.
    /// Email OTP is email-only. Password reset is email-only.
    /// </summary>
    private static bool IsInAppType(NotificationType type) => type switch
    {
        NotificationType.EmailOtp => false,
        NotificationType.PasswordReset => false,
        _ => true
    };

    /// <summary>
    /// Types that also trigger a phone push simulation per the FRD notification matrix.
    /// No-show is email-only for phone; OTP and password reset are email-only.
    /// </summary>
    private static bool IsPhonePushType(NotificationType type) => type switch
    {
        NotificationType.CarApproved => true,
        NotificationType.CarRejected => true,
        NotificationType.BookingRequest => true,
        NotificationType.BookingAccepted => true,
        NotificationType.BookingRejected => true,
        NotificationType.BookingAutoCancelled => true,
        NotificationType.DriverArrivedAtStop => true,
        NotificationType.RideEnded => true,
        NotificationType.RideCancelledByDriver => true,
        NotificationType.WarningIssued => true,
        NotificationType.AccountSuspended => true,
        NotificationType.CashReceived => true,
        _ => false
    };
}
