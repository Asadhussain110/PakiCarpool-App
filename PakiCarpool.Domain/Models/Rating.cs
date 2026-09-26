namespace PakiCarpool.Domain.Models;

/// <summary>
/// Post-ride rating. Stars 1-5, optional review text (max 300 chars).
/// Positive ratings (4-5 stars) add +3 trust points.
/// </summary>
public class Rating
{
    public Guid Id { get; set; }
    public Guid RideId { get; set; }
    public Guid RaterId { get; set; }
    public Guid RateeId { get; set; }
    public int Stars { get; set; }
    public string? ReviewText { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
