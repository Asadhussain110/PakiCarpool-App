using PakiCarpool.Domain.Enums;

namespace PakiCarpool.Domain.Models;

/// <summary>
/// A passenger's booking on a ride. Scoped to a pickup→dropoff segment.
/// Max one active booking per passenger. Cancellation is classified for trust scoring.
/// </summary>
public class Booking
{
    public Guid Id { get; set; }
    public Guid RideId { get; set; }
    public Guid PassengerId { get; set; }
    public Guid PickupStopId { get; set; }
    public Guid DropoffStopId { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool IsCashPaid { get; set; }
    public string? CancelReason { get; set; }
    public CancellationClassification? CancelClassification { get; set; }

    public Ride Ride { get; set; } = null!;
    public User Passenger { get; set; } = null!;
    public RideStop PickupStop { get; set; } = null!;
    public RideStop DropoffStop { get; set; } = null!;
    public ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();
}
