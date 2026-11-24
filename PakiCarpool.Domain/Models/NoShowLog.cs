namespace PakiCarpool.Domain.Models;

/// <summary>
/// Records a no-show event. Reporter files against offender.
/// One no-show per booking — BookingId carries a unique index so repeated or
/// concurrent reports for the same booking can never double-penalize.
/// Nullable so any legacy rows written before this column existed keep NULL
/// and stay exempt from the unique index (PostgreSQL allows multiple NULLs).
/// No-shows deduct 10 trust points from the offender.
/// </summary>
public class NoShowLog
{
    public Guid Id { get; set; }
    public Guid ReporterId { get; set; }
    public Guid OffenderId { get; set; }
    public Guid RideId { get; set; }
    public Guid? BookingId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
