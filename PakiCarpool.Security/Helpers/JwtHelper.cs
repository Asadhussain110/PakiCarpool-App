using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using PakiCarpool.Common.Constants;

namespace PakiCarpool.Security.Helpers;

/// <summary>
/// JWT token generation and validation helper.
/// Access-token lifetimes are role-specific and read from Auth configuration.
/// Partial tokens: used for driver liveness pending and admin step-1 states.
/// Algorithm: HS256 with secret from environment variable.
/// </summary>
public class JwtHelper
{
    public const string LivenessPendingClaim = "livenessPending";
    public static readonly TimeSpan ValidationClockSkew = TimeSpan.Zero;

    private readonly IConfiguration _configuration;
    private readonly string _secret;
    private readonly string _issuer;
    private readonly string _audience;

    public JwtHelper(IConfiguration configuration)
    {
        _configuration = configuration;
        _secret = _configuration["JWT_SECRET"]
            ?? _configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("JWT secret is not configured.");
        _issuer = _configuration["JWT_ISSUER"]
            ?? _configuration["Jwt:Issuer"]
            ?? "api.pakicarpool.pk";
        _audience = _configuration["JWT_AUDIENCE"]
            ?? _configuration["Jwt:Audience"]
            ?? "pakicarpool-clients";
    }

    /// <summary>
    /// Generates a full access token using the configured expiry for the user's role.
    /// Claims: userId, email, role, jti, iat.
    /// </summary>
    public string GenerateAccessToken(Guid userId, string email, string role)
        => GenerateAccessToken(userId, email, role, out _);

    /// <summary>
    /// Generates a full access token and returns the same UTC timestamp carried by its signed iat claim.
    /// </summary>
    public string GenerateAccessToken(Guid userId, string email, string role, out DateTime sessionIssuedAt)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("userId", userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(ClaimTypes.Role, role),
            new("role", role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        claims.RemoveAll(claim => claim.Type == LivenessPendingClaim);
        var expiryMinutes = role switch
        {
            "Passenger" => _configuration.GetValue<int>("Auth:PassengerTokenExpiryMinutes"),
            "Driver" => _configuration.GetValue<int>("Auth:DriverTokenExpiryMinutes"),
            _ => throw new ArgumentException("Only passengers and drivers receive user access tokens.", nameof(role))
        };

        return GenerateToken(claims, expiryMinutes, out sessionIssuedAt);
    }

    /// <summary>
    /// Generates an admin-specific access token using configured expiry; no refresh token is issued.
    /// Claims: userId, email, role=Admin, jti.
    /// </summary>
    public string GenerateAdminToken(Guid userId, string email, int authTokenVersion = 0)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("userId", userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(ClaimTypes.Role, RoleNames.Admin),
            new("role", RoleNames.Admin),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("authTokenVersion", authTokenVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };

        return GenerateToken(claims, _configuration.GetValue<int>("Auth:AdminTokenExpiryMinutes"));
    }

    /// <summary>
    /// Generates a partial token with a specific claim (e.g., livenessPending, adminStep1).
    /// Uses the configured partial-token expiry and is restricted to partial operations only.
    /// </summary>
    public string GeneratePartialToken(
        Guid userId,
        string email,
        string role,
        string partialClaimName,
        int? expiryMinutes = null,
        int? authTokenVersion = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("userId", userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(ClaimTypes.Role, role),
            new("role", role),
            new(partialClaimName, "true"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (authTokenVersion.HasValue)
        {
            claims.Add(new Claim(
                "authTokenVersion",
                authTokenVersion.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        var configuredExpiry = expiryMinutes ?? _configuration.GetValue<int>("Auth:PartialTokenExpiryMinutes");
        return GenerateToken(claims, Math.Max(1, configuredExpiry));
    }

    /// <summary>
    /// Validates a JWT token and returns the ClaimsPrincipal if valid.
    /// Returns null if the token is invalid or expired.
    /// </summary>
    public ClaimsPrincipal? ValidateToken(string token)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var tokenHandler = new JwtSecurityTokenHandler();

        try
        {
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateLifetime = true,
                ClockSkew = ValidationClockSkew
            };

            return tokenHandler.ValidateToken(token, validationParameters, out _);
        }
        catch
        {
            return null;
        }
    }

    private string GenerateToken(List<Claim> claims, int expiryMinutes)
        => GenerateToken(claims, expiryMinutes, out _);

    private string GenerateToken(List<Claim> claims, int expiryMinutes, out DateTime issuedAt)
    {
        issuedAt = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()).UtcDateTime;
        claims.RemoveAll(claim => claim.Type == JwtRegisteredClaimNames.Iat);
        claims.Add(new Claim(
            JwtRegisteredClaimNames.Iat,
            new DateTimeOffset(issuedAt).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ClaimValueTypes.Integer64));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: issuedAt.AddMinutes(expiryMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
