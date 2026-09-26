namespace PakiCarpool.Domain.Models;

/// <summary>
/// Real-time GPS location of a driver during an active ride.
/// Uses upsert pattern: one active record per driver per ride.
/// </summary>
public class DriverLocation
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }
    public Guid RideId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
