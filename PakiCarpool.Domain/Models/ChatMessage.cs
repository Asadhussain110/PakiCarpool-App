namespace PakiCarpool.Domain.Models;

/// <summary>
/// Chat message scoped to one booking (one passenger ↔ their driver).
/// Messages expire 7 days after sending. Chat only active while booking is Active.
/// </summary>
public class ChatMessage
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid SenderId { get; set; }
    public Guid ReceiverId { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public bool IsDelivered { get; set; }

    public Booking Booking { get; set; } = null!;
}
