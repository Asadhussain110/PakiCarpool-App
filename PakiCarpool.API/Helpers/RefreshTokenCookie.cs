using Microsoft.AspNetCore.Http;

namespace PakiCarpool.API.Helpers;

/// <summary>
/// Applies the same refresh-cookie policy to authentication and liveness-completed login responses.
/// </summary>
public static class RefreshTokenCookie
{
    public const string Name = "refreshToken";
    public const string Path = "/api/v1/auth";

    public static void Append(HttpResponse response, string rawRefreshToken, int expiryDays, bool secure)
    {
        response.Cookies.Append(Name, rawRefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(expiryDays),
            Path = Path
        });
    }

    public static void Delete(HttpResponse response, bool secure)
    {
        response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = Path
        });
    }
}
