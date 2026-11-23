using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.API.Helpers;

/// <summary>
/// Rechecks persisted account status after JWT validation for API and SignalR access.
/// </summary>
public static class AccountAccessValidator
{
    /// <summary>
    /// Returns the token's active user, or null when the identity is invalid, inactive, or suspended.
    /// </summary>
    public static async Task<User?> GetActiveUserAsync(
        ClaimsPrincipal? principal,
        IUserRepository userRepository,
        CancellationToken cancellationToken = default)
    {
        var userIdValue = principal?.FindFirstValue("userId")
            ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal?.FindFirstValue("sub");
        if (!Guid.TryParse(userIdValue, out var userId))
            return null;

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        return user is { IsActive: true, IsSuspended: false } ? user : null;
    }

    /// <summary>
    /// Applies current account status and admin token-version checks after bearer-token validation.
    /// </summary>
    public static async Task ValidateTokenAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var jti = principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (!string.IsNullOrWhiteSpace(jti))
        {
            var tokenRevocationService = context.HttpContext.RequestServices
                .GetRequiredService<ITokenRevocationService>();
            if (await tokenRevocationService.IsRevokedAsync(jti))
            {
                context.Fail("Token has been revoked.");
                return;
            }
        }

        var userRepository = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
        var user = await GetActiveUserAsync(
            principal,
            userRepository,
            context.HttpContext.RequestAborted);
        if (user is null)
        {
            context.Fail("Account is inactive or suspended.");
            return;
        }

        if (principal?.IsInRole(RoleNames.Admin) != true)
            return;

        var versionValue = principal.FindFirst("authTokenVersion")?.Value ?? "0";
        if (!int.TryParse(
                versionValue,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var tokenVersion))
        {
            context.Fail("Invalid admin token version.");
            return;
        }

        var currentVersion = await userRepository.GetAuthTokenVersionAsync(
            user.Id,
            context.HttpContext.RequestAborted);
        if (currentVersion is null || currentVersion.Value != tokenVersion)
            context.Fail("Admin session was invalidated.");
    }
}
