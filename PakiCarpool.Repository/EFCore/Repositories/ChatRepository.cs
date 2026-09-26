using Microsoft.EntityFrameworkCore;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Repository.EFCore;

namespace PakiCarpool.Repository.EFCore.Repositories;

/// <summary>
/// EF Core implementation of IChatRepository.
/// Chat is scoped to one booking (one passenger ↔ their driver). Messages expire after 7 days.
/// </summary>
public class ChatRepository : IChatRepository
{
    private readonly PakiCarpoolDbContext _context;

    public ChatRepository(PakiCarpoolDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<List<ChatMessage>> GetMessagesByBookingIdAsync(Guid bookingId)
    {
        return await _context.ChatMessages
            .Where(cm => cm.BookingId == bookingId && cm.ExpiresAt > DateTime.UtcNow)
            .OrderBy(cm => cm.SentAt)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<ChatMessage> CreateAsync(ChatMessage message)
    {
        _context.ChatMessages.Add(message);
        await _context.SaveChangesAsync();
        return message;
    }

    /// <inheritdoc />
    public async Task MarkAsDeliveredAsync(Guid messageId)
    {
        var message = await _context.ChatMessages.FindAsync(messageId);
        if (message != null)
        {
            message.IsDelivered = true;
            await _context.SaveChangesAsync();
        }
    }

    /// <inheritdoc />
    public async Task DeleteExpiredMessagesAsync()
    {
        var expired = await _context.ChatMessages
            .Where(cm => cm.ExpiresAt < DateTime.UtcNow)
            .ToListAsync();

        _context.ChatMessages.RemoveRange(expired);
        await _context.SaveChangesAsync();
    }
}
