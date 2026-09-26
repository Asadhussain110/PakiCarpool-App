using Microsoft.AspNetCore.SignalR;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Chat;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.Services.Implementations;

/// <summary>
/// Chat is strictly scoped to one booking (one passenger ↔ their driver).
/// Messages are only writable while Booking.Status == Active.
/// History is readable by participants for 7 days, then purged by ChatExpiryJob.
/// </summary>
public class ChatService : IChatService
{
    private readonly IChatRepository _chatRepository;
    private readonly IBookingRepository _bookingRepository;
    private readonly IRideRepository _rideRepository;
    private readonly IUserRepository _userRepository;
    private readonly IHubContext<Microsoft.AspNetCore.SignalR.Hub> _hubContext;

    public ChatService(
        IChatRepository chatRepository,
        IBookingRepository bookingRepository,
        IRideRepository rideRepository,
        IUserRepository userRepository,
        IHubContext<Microsoft.AspNetCore.SignalR.Hub> hubContext)
    {
        _chatRepository = chatRepository;
        _bookingRepository = bookingRepository;
        _rideRepository = rideRepository;
        _userRepository = userRepository;
        _hubContext = hubContext;
    }

    /// <summary>
    /// Persists a chat message. Validates sender is a participant and booking is Active.
    /// Sets ExpiresAt = SentAt + 7 days. Delivers to the receiver's personal SignalR group.
    /// </summary>
    public async Task<ApiResponse<ChatMessageResponseDto>> SendMessageAsync(Guid senderId, ChatMessageDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Message))
            return new ApiResponse<ChatMessageResponseDto> { Message = ErrorCodes.ValidationError };

        if (dto.Message.Length > AppConstants.MaxChatMessageLength)
            return new ApiResponse<ChatMessageResponseDto>
            {
                Message = $"Message cannot exceed {AppConstants.MaxChatMessageLength} characters."
            };

        var booking = await _bookingRepository.GetByIdAsync(dto.BookingId, includeRide: true);
        if (booking is null)
            return new ApiResponse<ChatMessageResponseDto> { Message = ErrorCodes.NotFound };

        if (booking.Status != BookingStatus.Active)
            return new ApiResponse<ChatMessageResponseDto>
            {
                Message = "Chat is only available while the booking is Active."
            };

        var (isParticipant, receiverId) = await ResolveParticipantsAsync(booking, senderId);
        if (!isParticipant || receiverId is null)
            return new ApiResponse<ChatMessageResponseDto> { Message = ErrorCodes.Forbidden };

        var now = DateTime.UtcNow;
        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            SenderId = senderId,
            ReceiverId = receiverId.Value,
            Message = dto.Message.Trim(),
            SentAt = now,
            ExpiresAt = now.AddDays(AppConstants.ChatExpiryDays),
            IsDelivered = false
        };

        var created = await _chatRepository.CreateAsync(message);

        await _hubContext.Clients
            .Group($"user-{receiverId.Value}")
            .SendAsync("ReceiveChatMessage", new
            {
                bookingId = created.BookingId,
                senderId = created.SenderId,
                message = created.Message,
                sentAt = created.SentAt
            });

        created.IsDelivered = true;
        await _chatRepository.MarkAsDeliveredAsync(created.Id);

        return ApiResponse<ChatMessageResponseDto>.Success(MapToResponse(created), "Message sent.");
    }

    /// <summary>
    /// Retrieves chat history for a booking. Only accessible by the two participants.
    /// Returns messages still within the 7-day retention window.
    /// </summary>
    public async Task<ApiResponse<ChatHistoryResponseDto>> GetChatHistoryAsync(Guid userId, Guid bookingId)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId, includeRide: true);
        if (booking is null)
            return new ApiResponse<ChatHistoryResponseDto> { Message = ErrorCodes.NotFound };

        var (isParticipant, _) = await ResolveParticipantsAsync(booking, userId);
        if (!isParticipant)
            return new ApiResponse<ChatHistoryResponseDto> { Message = ErrorCodes.Forbidden };

        var messages = await _chatRepository.GetMessagesByBookingIdAsync(bookingId);
        var history = new ChatHistoryResponseDto
        {
            BookingId = bookingId,
            Messages = messages.Select(MapToResponse).ToList()
        };

        return ApiResponse<ChatHistoryResponseDto>.Success(history);
    }

    /// <summary>
    /// Resolves whether <paramref name="userId"/> is the passenger or the ride's driver,
    /// and returns the counterpart's user ID as the chat receiver.
    /// </summary>
    private async Task<(bool IsParticipant, Guid? ReceiverId)> ResolveParticipantsAsync(
        Booking booking, Guid userId)
    {
        if (booking.PassengerId == userId)
        {
            var ride = booking.Ride ?? await _rideRepository.GetByIdAsync(booking.RideId);
            if (ride is null) return (false, null);

            var driverProfile = await _userRepository.GetDriverProfileByIdAsync(ride.DriverId);
            if (driverProfile is null) return (false, null);

            return (true, driverProfile.UserId);
        }

        var rideForDriver = booking.Ride ?? await _rideRepository.GetByIdAsync(booking.RideId);
        if (rideForDriver is null) return (false, null);

        var profile = await _userRepository.GetDriverProfileByUserIdAsync(userId);
        if (profile is null || profile.Id != rideForDriver.DriverId)
            return (false, null);

        return (true, booking.PassengerId);
    }

    private static ChatMessageResponseDto MapToResponse(ChatMessage message) =>
        new()
        {
            Id = message.Id,
            BookingId = message.BookingId,
            SenderId = message.SenderId,
            ReceiverId = message.ReceiverId,
            Message = message.Message,
            SentAt = message.SentAt,
            IsDelivered = message.IsDelivered
        };
}
