using System.ComponentModel.DataAnnotations;

namespace PakiCarpool.Domain.DTOs.Passenger;

public class CreateBookingDto
{
    [Required]
    public Guid RideId { get; set; }

    [Required]
    public Guid PickupStopId { get; set; }

    [Required]
    public Guid DropoffStopId { get; set; }
}

public class CancelBookingDto
{
    [Required]
    [StringLength(500, MinimumLength = 10)]
    public string Reason { get; set; } = string.Empty;
}

public class ActiveBookingResponseDto
{
    public Guid Id { get; set; }
    public Guid RideId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public string CarMake { get; set; } = string.Empty;
    public string CarModel { get; set; } = string.Empty;
    public string PickupStopName { get; set; } = string.Empty;
    public string DropoffStopName { get; set; } = string.Empty;
    public decimal Fare { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public double? DriverLat { get; set; }
    public double? DriverLng { get; set; }
    public int? EtaSeconds { get; set; }
}
