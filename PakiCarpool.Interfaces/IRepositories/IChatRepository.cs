using PakiCarpool.Domain.Models;

namespace PakiCarpool.Interfaces.IRepositories;

/// <summary>
/// Repository for ChatMessage operations.
/// Chat is scoped to one booking (one passenger ↔ their driver). Messages expire after 7 days.
/// </summary>
public interface IChatRepository
{
    /// <summary>
    /// Gets chat messages for a booking within the 7-day window.
    /// </summary>
    Task<List<ChatMessage>> GetMessagesByBookingIdAsync(Guid bookingId);

    /// <summary>
    /// Creates a new chat message.
    /// </summary>
    Task<ChatMessage> CreateAsync(ChatMessage message);

    /// <summary>
    /// Marks a message as delivered.
    /// </summary>
    Task MarkAsDeliveredAsync(Guid messageId);

    /// <summary>
    /// Deletes all expired chat messages (ExpiresAt < now).
    /// Used by ChatExpiryJob.
    /// </summary>
    Task DeleteExpiredMessagesAsync();
}
