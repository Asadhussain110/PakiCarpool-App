namespace PakiCarpool.Domain.Models;

/// <summary>
/// Records a no-show event. Reporter files against offender.
/// No-shows deduct 10 trust points from the offender.
/// </summary>
public class NoShowLog
{
    public Guid Id { get; set; }
    public Guid ReporterId { get; set; }
    public Guid OffenderId { get; set; }
    public Guid RideId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
