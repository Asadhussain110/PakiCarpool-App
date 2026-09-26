using OtpNet;

namespace PakiCarpool.Security.Helpers;

/// <summary>
/// TOTP helper for admin 2FA using OtpNet.
/// Generates secrets and verifies time-based codes with VerificationWindow(1,1).
/// </summary>
public static class TotpHelper
{
    /// <summary>
    /// Generates a new random 20-byte TOTP secret.
    /// Returns the secret as a Base32-encoded string suitable for QR code generation.
    /// </summary>
    public static string GenerateSecret()
    {
        var secret = KeyGeneration.GenerateRandomKey(20);
        return Base32Encoding.ToString(secret);
    }

    /// <summary>
    /// Verifies a TOTP code against a Base32-encoded secret.
    /// Uses VerificationWindow(1, 1) to allow ±1 time step tolerance.
    /// </summary>
    public static bool VerifyCode(string base32Secret, string code)
    {
        var secretBytes = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(secretBytes);
        return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
    }
}
