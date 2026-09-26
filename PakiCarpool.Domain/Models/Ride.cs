using PakiCarpool.Domain.Enums;

namespace PakiCarpool.Domain.Models;

/// <summary>
/// A ride posted by a driver. Only one Posted/Active ride allowed per driver.
/// CancelGraceExpiresAt = CreatedAt + 5 minutes for penalty-free cancellation.
/// </summary>
public class Ride
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }
    public Guid CarId { get; set; }
    public decimal Fare { get; set; }
    public int MaxSeats { get; set; }
    public RideStatus Status { get; set; } = RideStatus.Posted;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime CancelGraceExpiresAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CancelReason { get; set; }

    public DriverProfile Driver { get; set; } = null!;
    public DriverCar Car { get; set; } = null!;
    public ICollection<RideStop> Stops { get; set; } = new List<RideStop>();
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
