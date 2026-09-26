using PakiCarpool.Domain.Enums;

namespace PakiCarpool.Domain.Models;

/// <summary>
/// Audit log for ride/booking cancellations. EntityType is polymorphic ("Booking" or "Ride").
/// Tracks classification and trust points deducted for frivolous cancellations.
/// </summary>
public class CancellationLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid EntityId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public CancellationClassification Classification { get; set; }
    public int TrustPointsDeducted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
