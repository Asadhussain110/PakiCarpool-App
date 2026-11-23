namespace PakiCarpool.Domain.Models;

/// <summary>
/// One-time challenge nonce issued to a driver before liveness submission.
/// A nonce is valid for <c>Liveness:NonceTtlMinutes</c> minutes and consumed
/// (IsUsed = true) immediately after the liveness result is processed,
/// regardless of pass or fail.
/// </summary>
public class LivenessChallenge
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to <see cref="User"/>. The driver that was issued this challenge.</summary>
    public Guid DriverId { get; set; }

    /// <summary>Cryptographically random 32-byte value encoded as Base64Url.</summary>
    public string Nonce { get; set; } = string.Empty;

    /// <summary>JSON array containing the ordered, server-selected prompts for this challenge.</summary>
    public string PromptsJson { get; set; } = "[]";

    /// <summary>UTC timestamp when the challenge was created.</summary>
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp after which the challenge must be rejected.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// True once the nonce has been consumed (pass or fail).
    /// Prevents replay attacks.
    /// </summary>
    public bool IsUsed { get; set; } = false;

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Navigation property to the owning user.</summary>
    public User Driver { get; set; } = null!;
}
