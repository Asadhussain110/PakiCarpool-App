using System.ComponentModel.DataAnnotations;

namespace PakiCarpool.Domain.DTOs.Driver;

public class RegisterCarDto
{
    [Required]
    [StringLength(50)]
    public string Make { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Model { get; set; } = string.Empty;

    [Required]
    [Range(2000, 2030)]
    public int Year { get; set; }

    [Required]
    [StringLength(20)]
    public string LicensePlate { get; set; } = string.Empty;

    [Required]
    [Range(1, 6)]
    public int AvailableSeats { get; set; }

    [Required]
    [MinLength(1)]
    public List<string> PhotoPaths { get; set; } = new();

    [Required]
    public string RegDocPath { get; set; } = string.Empty;
}

public class UpdateCarDto
{
    [StringLength(50)]
    public string? Make { get; set; }

    [StringLength(50)]
    public string? Model { get; set; }

    [Range(2000, 2030)]
    public int? Year { get; set; }

    [StringLength(20)]
    public string? LicensePlate { get; set; }

    [Range(1, 6)]
    public int? AvailableSeats { get; set; }

    public List<string>? PhotoPaths { get; set; }
    public string? RegDocPath { get; set; }
}

public class PostRideDto
{
    [Required]
    [MinLength(2)]
    public List<RideStopDto> Stops { get; set; } = new();

    [Required]
    [Range(0.01, 100000)]
    public decimal Fare { get; set; }

    [Required]
    [Range(1, 6)]
    public int MaxSeats { get; set; }
}

public class RideStopDto
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Range(-90, 90)]
    public double Lat { get; set; }

    [Required]
    [Range(-180, 180)]
    public double Lng { get; set; }

    [Required]
    [Range(1, 100)]
    public int SequenceOrder { get; set; }
}

public class LivenessResultDto
{
    [Required]
    public string LivenessResult { get; set; } = string.Empty;

    [Required]
    public string SelfieBase64 { get; set; } = string.Empty;
}

public class CancelRideDto
{
    [Required]
    [StringLength(500, MinimumLength = 10)]
    public string Reason { get; set; } = string.Empty;
}

public class DriverLocationDto
{
    [Required]
    public Guid RideId { get; set; }

    [Required]
    [Range(-90, 90)]
    public double Lat { get; set; }

    [Required]
    [Range(-180, 180)]
    public double Lng { get; set; }
}

public class RideResponseDto
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public decimal DriverRating { get; set; }
    public string CarMake { get; set; } = string.Empty;
    public string CarModel { get; set; } = string.Empty;
    public decimal Fare { get; set; }
    public int MaxSeats { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<RideStopResponseDto> Stops { get; set; } = new();
}

public class RideStopResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public int SequenceOrder { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? ArrivedAt { get; set; }
    public int? EtaSeconds { get; set; }
}

public class CarStatusResponseDto
{
    public Guid Id { get; set; }
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? RejectionReason { get; set; }
    public DateTime? ApprovedAt { get; set; }
}

public class BookingDetailDto
{
    public Guid Id { get; set; }
    public Guid PassengerId { get; set; }
    public string PassengerName { get; set; } = string.Empty;
    public string PickupStopName { get; set; } = string.Empty;
    public string DropoffStopName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public bool IsCashPaid { get; set; }
}
