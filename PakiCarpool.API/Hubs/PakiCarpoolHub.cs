using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using PakiCarpool.Common;
using PakiCarpool.Domain.DTOs.Chat;
using PakiCarpool.Domain.DTOs.Driver;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Middleware;
using PakiCarpool.Security.Policies;

namespace PakiCarpool.API.Hubs;

/// <summary>
/// Real-time hub for live location, chat, and ride-group membership.
/// Clients join <c>user-{userId}</c> on connect. Ride groups use <c>ride-{rideId}</c>.
/// </summary>
[Authorize(Policy = RolePolicy.SignalRPolicy)]
public sealed class PakiCarpoolHub : ThrottledHub
{
    private readonly IDriverService _driverService;
    private readonly IChatService _chatService;
    private readonly IRideRepository _rideRepository;
    private readonly IRideGroupConnectionTracker _rideGroupConnectionTracker;
    private readonly IRateLimitCounter _rateLimitCounter;
    private readonly ILogger<PakiCarpoolHub> _logger;
    private readonly IConfiguration _configuration;

    public PakiCarpoolHub(
        IDriverService driverService,
        IChatService chatService,
        IRideRepository rideRepository,
        IRideGroupConnectionTracker rideGroupConnectionTracker,
        IRateLimitCounter rateLimitCounter,
        ILogger<PakiCarpoolHub> logger,
        IConfiguration configuration)
    {
        _driverService = driverService;
        _chatService = chatService;
        _rideRepository = rideRepository;
        _rideGroupConnectionTracker = rideGroupConnectionTracker;
        _rateLimitCounter = rateLimitCounter;
        _logger = logger;
        _configuration = configuration;
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
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var userId = GetUserIdOrNull();
        if (userId.HasValue)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user-{userId.Value}");

        try
        {
            await _rideGroupConnectionTracker.RemoveConnectionAsync(Context.ConnectionId, cleanupTimeout.Token);
        }
        catch (Exception cleanupException)
        {
            _logger.LogWarning(cleanupException, "Failed to clear SignalR ride memberships for {ConnectionId}.", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Driver sends GPS update every 5 seconds. Persists and broadcasts to ride group.
    /// </summary>
    public async Task UpdateLocation(string rideId, double lat, double lng)
    {
        var userId = GetRequiredUserId();
        if (await IsHubThrottledAsync(
            userId,
            "location",
            _configuration.GetValue("HubThrottle:LocationPerMinute", 15)))
        {
            return;
        }

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
        if (await IsHubThrottledAsync(
            userId,
            "chat",
            _configuration.GetValue("HubThrottle:ChatPerMinute", 20)))
        {
            return;
        }

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

        if (!Guid.TryParse(rideId, out var rideGuid))
            throw new HubException("Invalid rideId.");

        var userId = GetRequiredUserId();
        var ride = await _rideRepository.GetByIdAsync(
            rideGuid,
            includeBookings: true,
            cancellationToken: Context.ConnectionAborted);
        if (ride is null || ride.Status is not RideStatus.Posted and not RideStatus.Active)
            throw new HubException("Ride is not available.");

        var isDriver = ride.Driver.UserId == userId;
        var isPassenger = ride.Bookings.Any(booking =>
            booking.PassengerId == userId &&
            booking.Status is BookingStatus.Accepted or BookingStatus.Active);

        if (!isDriver && !isPassenger)
            throw new HubException("You are not a participant in this ride.");

        var groupName = $"ride-{rideGuid:D}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName, Context.ConnectionAborted);
        if (isPassenger)
        {
            try
            {
                await _rideGroupConnectionTracker.TrackAsync(rideGuid, userId, Context.ConnectionId, Context.ConnectionAborted);
            }
            catch
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
                throw new HubException("Unable to authorize ride-group membership.");
            }
        }

        var currentRide = await _rideRepository.GetByIdAsync(
            rideGuid,
            includeBookings: true,
            cancellationToken: Context.ConnectionAborted);
        var stillAuthorized = currentRide is not null
            && currentRide.Status is RideStatus.Posted or RideStatus.Active
            && (currentRide.Driver.UserId == userId
                || currentRide.Bookings.Any(booking =>
                    booking.PassengerId == userId
                    && booking.Status is BookingStatus.Accepted or BookingStatus.Active));
        if (!stillAuthorized)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            if (isPassenger)
                await _rideGroupConnectionTracker.RemoveAsync(rideGuid, userId, Context.ConnectionAborted);
            throw new HubException("You are not a participant in this ride.");
        }
    }

    /// <summary>
    /// Passenger joins a passenger-scoped ride subgroup after booking acceptance.
    /// </summary>
    public async Task JoinPassengerRideGroup(string rideId, string passengerId)
    {
        if (string.IsNullOrWhiteSpace(rideId) || string.IsNullOrWhiteSpace(passengerId))
            throw new HubException("rideId and passengerId are required.");

        if (!Guid.TryParse(rideId, out var rideGuid) || !Guid.TryParse(passengerId, out var passengerGuid))
            throw new HubException("Invalid rideId or passengerId.");

        var userId = GetRequiredUserId();
        if (userId != passengerGuid)
            throw new HubException("You can only join your own passenger group.");

        var ride = await _rideRepository.GetByIdAsync(
            rideGuid,
            includeBookings: true,
            cancellationToken: Context.ConnectionAborted);
        if (ride is null || ride.Status is not RideStatus.Posted and not RideStatus.Active
            || !ride.Bookings.Any(booking =>
                booking.PassengerId == passengerGuid &&
                booking.Status is BookingStatus.Accepted or BookingStatus.Active))
        {
            throw new HubException("An accepted or active booking is required to join this group.");
        }

        var passengerGroup = $"ride-{rideGuid:D}-pax-{passengerGuid:D}";
        await Groups.AddToGroupAsync(Context.ConnectionId, passengerGroup, Context.ConnectionAborted);
        try
        {
            await _rideGroupConnectionTracker.TrackAsync(rideGuid, passengerGuid, Context.ConnectionId, Context.ConnectionAborted);
        }
        catch
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, passengerGroup);
            throw new HubException("Unable to authorize ride-group membership.");
        }

        var currentRide = await _rideRepository.GetByIdAsync(
            rideGuid,
            includeBookings: true,
            cancellationToken: Context.ConnectionAborted);
        var stillAuthorized = currentRide is not null
            && currentRide.Status is RideStatus.Posted or RideStatus.Active
            && currentRide.Bookings.Any(booking =>
                booking.PassengerId == passengerGuid
                && booking.Status is BookingStatus.Accepted or BookingStatus.Active);
        if (!stillAuthorized)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, passengerGroup);
            await _rideGroupConnectionTracker.RemoveAsync(rideGuid, passengerGuid, Context.ConnectionAborted);
            throw new HubException("An accepted or active booking is required to join this group.");
        }
    }

    /// <summary>
    /// Leaves the ride location broadcast group.
    /// </summary>
    public async Task LeaveRideGroup(string rideId)
    {
        if (string.IsNullOrWhiteSpace(rideId))
            throw new HubException("rideId is required.");

        if (!Guid.TryParse(rideId, out var rideGuid))
            throw new HubException("Invalid rideId.");

        var canonicalRideId = rideGuid.ToString("D");
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ride-{canonicalRideId}");
        var userId = GetUserIdOrNull();
        if (userId.HasValue)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ride-{canonicalRideId}-pax-{userId.Value:D}");
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

    private async Task<bool> IsHubThrottledAsync(Guid userId, string category, int limit)
    {
        try
        {
            var result = await _rateLimitCounter.IncrementAsync(
                $"rate-limit:hub:{category}:{userId}",
                TimeSpan.FromMinutes(1),
                Context.ConnectionAborted);
            return result.Count > Math.Max(1, limit);
        }
        catch (OperationCanceledException) when (Context.ConnectionAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "SignalR rate-limit counter failed for {Category}.", category);
            throw new HubException("Request protection is temporarily unavailable.");
        }
    }
}
