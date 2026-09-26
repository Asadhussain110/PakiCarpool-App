using System.Security.Cryptography;

namespace PakiCarpool.Common.Utilities;

/// <summary>
/// Generates cryptographically secure 6-digit OTPs for email verification.
/// Uses RandomNumberGenerator for security — never use System.Random for OTPs.
/// </summary>
public static class OtpGenerator
{
    /// <summary>
    /// Generates a 6-digit numeric OTP using a cryptographically secure RNG.
    /// </summary>
    public static string Generate(int length = 6)
    {
        var max = (int)Math.Pow(10, length);
        var otp = RandomNumberGenerator.GetInt32(0, max);
        return otp.ToString().PadLeft(length, '0');
    }
}
