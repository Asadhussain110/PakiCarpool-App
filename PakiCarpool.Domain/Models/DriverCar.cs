using PakiCarpool.Domain.Enums;

namespace PakiCarpool.Domain.Models;

/// <summary>
/// Vehicle registered by a driver. LicensePlate is AES-256 encrypted at rest.
/// Requires admin approval before use. AvailableSeats capped at 1-6.
/// </summary>
public class DriverCar
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; }
    public string LicensePlate { get; set; } = string.Empty;
    public int AvailableSeats { get; set; }
    public CarStatus Status { get; set; } = CarStatus.Pending;
    public string PhotoPaths { get; set; } = "[]";
    public string RegDocPath { get; set; } = string.Empty;
    public string? RejectionReason { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DriverProfile Driver { get; set; } = null!;
}
