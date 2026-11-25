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

public class RideSearchRequestDto
{
    public double StartLatitude { get; set; }
    public double StartLongitude { get; set; }
    public double EndLatitude { get; set; }
    public double EndLongitude { get; set; }
    public double RadiusMeters { get; set; } = 500;
}

public class RideSearchResultDto
{
    public Guid RideId { get; set; }
    public Guid DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public string DriverPhotoUrl { get; set; } = string.Empty;
    public RideStopMatchDto StartStop { get; set; } = new();
    public RideStopMatchDto EndStop { get; set; } = new();
    public int MatchRank { get; set; }
    public DateTime DeparturTime { get; set; }
    public int AvailableSeats { get; set; }
}

public class RideStopMatchDto
{
    public Guid StopId { get; set; }
    public string StopName { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool IsPerfectMatch { get; set; }
    public double? DistanceMeters { get; set; }
}

public class RideSearchCandidateDto
{
    public Guid RideId { get; set; }
    public Guid DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public string? DriverPhotoPath { get; set; }
    public DateTime? DriverPhotoUpdatedAt { get; set; }
    public DateTime DepartureTime { get; set; }
    public int MaxSeats { get; set; }
    public int AvailableSeats { get; set; }
    public Guid PairStartStopId { get; set; }
    public Guid PairEndStopId { get; set; }
    public Guid StopId { get; set; }
    public string StopName { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int SequenceOrder { get; set; }
    public double StartDistanceMeters { get; set; }
    public double EndDistanceMeters { get; set; }
}

public class ActiveBookingResponseDto
{
    public Guid Id { get; set; }
    public Guid RideId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public string? DriverPhotoUrl { get; set; }
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
