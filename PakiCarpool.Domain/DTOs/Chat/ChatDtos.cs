using System.ComponentModel.DataAnnotations;

namespace PakiCarpool.Domain.DTOs.Chat;

public class ChatMessageDto
{
    [Required]
    public Guid BookingId { get; set; }

    [Required]
    [StringLength(1000, MinimumLength = 1)]
    public string Message { get; set; } = string.Empty;
}

public class ChatMessageResponseDto
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid SenderId { get; set; }
    public Guid ReceiverId { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public bool IsDelivered { get; set; }
}

public class ChatHistoryResponseDto
{
    public Guid BookingId { get; set; }
    public List<ChatMessageResponseDto> Messages { get; set; } = new();
}
