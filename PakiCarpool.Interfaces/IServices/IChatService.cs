using PakiCarpool.Domain.DTOs.Chat;
using PakiCarpool.Domain.DTOs.Common;

namespace PakiCarpool.Interfaces.IServices;

/// <summary>
/// Defines chat operations scoped to individual bookings.
/// Chat is strictly between one passenger and their driver for a specific booking.
/// Messages expire after 7 days.
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Persists a chat message. Validates sender is a participant and booking is Active.
    /// Sets ExpiresAt = SentAt + 7 days.
    /// </summary>
    Task<ApiResponse<ChatMessageResponseDto>> SendMessageAsync(Guid senderId, ChatMessageDto dto);

    /// <summary>
    /// Retrieves chat history for a booking. Only accessible by the two participants.
    /// Returns messages within 7-day window.
    /// </summary>
    Task<ApiResponse<ChatHistoryResponseDto>> GetChatHistoryAsync(Guid userId, Guid bookingId);
}
