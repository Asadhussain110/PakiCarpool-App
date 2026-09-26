using System.ComponentModel.DataAnnotations;
using PakiCarpool.Domain.Enums;

namespace PakiCarpool.Domain.DTOs.Admin;

public class RejectCarDto
{
    [Required]
    [StringLength(500, MinimumLength = 10)]
    public string RejectionReason { get; set; } = string.Empty;
}

public class BlockUserDto
{
    [Required]
    [StringLength(500, MinimumLength = 10)]
    public string Reason { get; set; } = string.Empty;
}

public class DismissFlagDto
{
    [StringLength(500)]
    public string? Notes { get; set; }
}

public class ActionFlagDto
{
    [Required]
    [StringLength(500, MinimumLength = 10)]
    public string Action { get; set; } = string.Empty;
}

public class DashboardDto
{
    public int TotalUsers { get; set; }
    public int TotalDrivers { get; set; }
    public int TotalPassengers { get; set; }
    public int TotalRides { get; set; }
    public int ActiveRides { get; set; }
    public int PendingCarApprovals { get; set; }
    public int OpenFlags { get; set; }
    public int SuspendedAccounts { get; set; }
}

public class UserListItemDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; }
    public bool IsSuspended { get; set; }
    public int TrustScore { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UserDetailDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; }
    public bool IsSuspended { get; set; }
    public int TrustScore { get; set; }
    public bool IsEmailVerified { get; set; }
    public DateTime CreatedAt { get; set; }
    public DriverProfileDetailDto? DriverProfile { get; set; }
}

public class DriverProfileDetailDto
{
    public Guid Id { get; set; }
    public bool LivenessVerified { get; set; }
    public string? ProfilePhotoPath { get; set; }
    public int LivenessFailCount { get; set; }
    public decimal AverageRating { get; set; }
    public List<CarApprovalDto> Cars { get; set; } = new();
}

public class CarApprovalDto
{
    public Guid Id { get; set; }
    public Guid DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; }
    public int AvailableSeats { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RegDocPath { get; set; } = string.Empty;
    public List<string> PhotoPaths { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class FlagListItemDto
{
    public Guid Id { get; set; }
    public Guid ReporterId { get; set; }
    public string ReporterName { get; set; } = string.Empty;
    public Guid ReporteeId { get; set; }
    public string ReporteeName { get; set; } = string.Empty;
    public Guid RideId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public FlagStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}
