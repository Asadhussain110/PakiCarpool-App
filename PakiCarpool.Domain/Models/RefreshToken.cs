namespace PakiCarpool.Domain.Models;

/// <summary>
/// Refresh token stored as SHA-256 hash. 7-day expiry with rotation on refresh.
/// Set as httpOnly, Secure, SameSite=Strict cookie.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}
