using PakiCarpool.Common.Constants;

namespace PakiCarpool.Security.Helpers;

/// <summary>
/// BCrypt password hashing and verification helper.
/// Work factor 12 per security specification — provides ~240ms hash time.
/// </summary>
public static class BCryptHelper
{
    /// <summary>
    /// Hashes a plaintext password using BCrypt with work factor 12.
    /// </summary>
    public static string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: AppConstants.BcryptWorkFactor);
    }

    /// <summary>
    /// Verifies a plaintext password against a BCrypt hash.
    /// </summary>
    public static bool VerifyPassword(string password, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }
}
