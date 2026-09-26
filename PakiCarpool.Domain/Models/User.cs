using PakiCarpool.Domain.Enums;

namespace PakiCarpool.Domain.Models;

/// <summary>
/// Core user entity. Role is immutable and set at registration.
/// Passwords are BCrypt-hashed (nullable for Google OAuth users).
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public UserRole Role { get; set; }
    public string? GoogleId { get; set; }
    public bool IsEmailVerified { get; set; }
    public string? EmailOtp { get; set; }
    public DateTime? EmailOtpExpiry { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsSuspended { get; set; }
    public int TrustScore { get; set; } = 100;
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutUntil { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
