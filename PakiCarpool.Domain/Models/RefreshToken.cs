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

    /// <summary>The signed iat boundary of the access token paired with this refresh token.</summary>
    public DateTime SessionIssuedAt { get; set; }

    public User User { get; set; } = null!;
}
