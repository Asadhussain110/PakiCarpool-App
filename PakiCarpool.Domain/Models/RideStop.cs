using PakiCarpool.Domain.Enums;

namespace PakiCarpool.Domain.Models;

/// <summary>
/// A stop along a ride route. SequenceOrder is 1-based and enforces travel order.
/// Status tracks whether the driver is travelling to or has arrived at the stop.
/// </summary>
public class RideStop
{
    public Guid Id { get; set; }
    public Guid RideId { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int SequenceOrder { get; set; }
    public RideStopStatus Status { get; set; } = RideStopStatus.Upcoming;
    public DateTime? ArrivedAt { get; set; }

    public Ride Ride { get; set; } = null!;
}
