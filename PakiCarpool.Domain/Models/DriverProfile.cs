namespace PakiCarpool.Domain.Models;

/// <summary>
/// Extended profile for drivers. Liveness must be verified at every login.
/// </summary>
public class DriverProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public bool LivenessVerified { get; set; }
    public string? ProfilePhotoPath { get; set; }
    public int LivenessFailCount { get; set; }
    public DateTime? LivenessBannedUntil { get; set; }
    public decimal AverageRating { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public ICollection<DriverCar> Cars { get; set; } = new List<DriverCar>();
}
