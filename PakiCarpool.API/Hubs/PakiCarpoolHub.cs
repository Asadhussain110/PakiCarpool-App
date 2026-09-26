using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PakiCarpool.Domain.DTOs.Chat;
using PakiCarpool.Domain.DTOs.Driver;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.API.Hubs;

/// <summary>
/// Real-time hub for live location, chat, and ride-group membership.
/// Clients join <c>user-{userId}</c> on connect. Ride groups use <c>ride-{rideId}</c>.
/// </summary>
[Authorize]
public sealed class PakiCarpoolHub : Hub
{
    private readonly IDriverService _driverService;
    private readonly IChatService _chatService;
    private readonly ILogger<PakiCarpoolHub> _logger;

    public PakiCarpoolHub(
        IDriverService driverService,
        IChatService chatService,
        ILogger<PakiCarpoolHub> logger)
    {
        _driverService = driverService;
        _chatService = chatService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserIdOrNull();
        if (userId.HasValue)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId.Value}");
            _logger.LogDebug("SignalR connected: {ConnectionId} → user-{UserId}", Context.ConnectionId, userId.Value);

            var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value 
                       ?? Context.User?.FindFirst("role")?.Value;

            if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, "admins");
            }
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserIdOrNull();
        if (userId.HasValue)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user-{userId.Value}");

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Driver sends GPS update every 5 seconds. Persists and broadcasts to ride group.
    /// </summary>
    public async Task UpdateLocation(string rideId, double lat, double lng)
    {
        var userId = GetRequiredUserId();
        if (!Guid.TryParse(rideId, out var rideGuid))
            throw new HubException("Invalid rideId.");

        var result = await _driverService.UpdateLocationAsync(userId, new DriverLocationDto
        {
            RideId = rideGuid,
            Lat = lat,
            Lng = lng
        });

        if (!result.IsSuccess)
            throw new HubException(result.Message);
    }

    /// <summary>
    /// Sends a chat message scoped to one booking. Persists and delivers to the counterpart.
    /// </summary>
    public async Task SendChatMessage(string bookingId, string receiverId, string message)
    {
        var userId = GetRequiredUserId();
        if (!Guid.TryParse(bookingId, out var bookingGuid))
            throw new HubException("Invalid bookingId.");

        if (string.IsNullOrWhiteSpace(message))
            throw new HubException("Message cannot be empty.");

        // receiverId is validated against booking participants inside ChatService.
        _ = receiverId;

        var result = await _chatService.SendMessageAsync(userId, new ChatMessageDto
        {
            BookingId = bookingGuid,
            Message = message.Trim()
        });

        if (!result.IsSuccess)
            throw new HubException(result.Message);
    }

    /// <summary>
    /// Passenger or driver joins the ride group for live location updates.
    /// </summary>
    public async Task JoinRideGroup(string rideId)
    {
        if (string.IsNullOrWhiteSpace(rideId))
            throw new HubException("rideId is required.");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"ride-{rideId}");
    }

    /// <summary>
    /// Passenger joins a passenger-scoped ride subgroup after booking acceptance.
    /// </summary>
    public async Task JoinPassengerRideGroup(string rideId, string passengerId)
    {
        if (string.IsNullOrWhiteSpace(rideId) || string.IsNullOrWhiteSpace(passengerId))
            throw new HubException("rideId and passengerId are required.");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"ride-{rideId}-pax-{passengerId}");
    }

    /// <summary>
    /// Leaves the ride location broadcast group.
    /// </summary>
    public async Task LeaveRideGroup(string rideId)
    {
        if (string.IsNullOrWhiteSpace(rideId))
            throw new HubException("rideId is required.");

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ride-{rideId}");
    }

    private Guid GetRequiredUserId()
    {
        var userId = GetUserIdOrNull();
        if (!userId.HasValue)
            throw new HubException("User is not authenticated.");

        return userId.Value;
    }

    private Guid? GetUserIdOrNull()
    {
        var raw = Context.User?.FindFirst("userId")?.Value
                  ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? Context.User?.FindFirst("sub")?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
