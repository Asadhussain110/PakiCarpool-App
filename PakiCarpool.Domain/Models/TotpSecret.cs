namespace PakiCarpool.Domain.Models;

/// <summary>
/// Admin TOTP secret for 2FA. Secret is AES-256 encrypted at rest.
/// Admin login is 2-step: credentials → TOTP verification.
/// </summary>
public class TotpSecret
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Secret { get; set; } = string.Empty;
    public bool IsVerified { get; set; }

    public User User { get; set; } = null!;
}
