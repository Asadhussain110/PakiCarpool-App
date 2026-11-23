using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Helpers;

namespace PakiCarpool.Security.Middleware;

/// <summary>
/// JWT authentication middleware. Extracts and validates Bearer tokens from the Authorization header.
/// Populates HttpContext.User with claims on successful validation.
/// Runs before authorization policies. Rejects invalid/expired tokens with 401.
/// </summary>
public class JwtMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _secret;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly ITokenRevocationService _tokenRevocationService;

    public JwtMiddleware(RequestDelegate next, IConfiguration configuration, ITokenRevocationService tokenRevocationService)
    {
        _next = next;
        _secret = configuration["JWT_SECRET"]
            ?? configuration["Jwt:Secret"]
            ?? Environment.GetEnvironmentVariable("JWT_SECRET")
            ?? throw new InvalidOperationException("JWT secret is not configured.");
        _issuer = configuration["JWT_ISSUER"]
            ?? configuration["Jwt:Issuer"]
            ?? Environment.GetEnvironmentVariable("JWT_ISSUER")
            ?? "api.pakicarpool.pk";
        _audience = configuration["JWT_AUDIENCE"]
            ?? configuration["Jwt:Audience"]
            ?? Environment.GetEnvironmentVariable("JWT_AUDIENCE")
            ?? "pakicarpool-clients";
        _tokenRevocationService = tokenRevocationService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();

        if (authHeader != null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..].Trim();
            await AttachUserToContextAsync(context, token);
        }

        await _next(context);
    }

    private async Task AttachUserToContextAsync(HttpContext context, string token)
    {
        try
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
            var tokenHandler = new JwtSecurityTokenHandler();

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateLifetime = true,
                ClockSkew = JwtHelper.ValidationClockSkew
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwtToken ||
                !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                return;
            }

            var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            if (!string.IsNullOrWhiteSpace(jti) && await _tokenRevocationService.IsRevokedAsync(jti))
            {
                return;
            }

            context.User = principal;
        }
        catch
        {
            // Token validation failed — context.User remains unauthenticated.
        }
    }
}
