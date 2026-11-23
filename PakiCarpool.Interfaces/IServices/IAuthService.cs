using PakiCarpool.Domain.DTOs.Auth;
using PakiCarpool.Domain.DTOs.Common;

namespace PakiCarpool.Interfaces.IServices;

/// <summary>
/// Defines all authentication and authorization operations.
/// Handles registration, login (email + Google OAuth), token management, and admin 2FA.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Registers a new user with email/password. Sends OTP for email verification.
    /// Creates DriverProfile if role is Driver.
    /// </summary>
    Task<ApiResponse<object>> RegisterAsync(RegisterRequestDto dto);

    /// <summary>
    /// Verifies email OTP. Sets IsEmailVerified = true on success and returns a liveness-pending
    /// partial token for Driver accounts.
    /// </summary>
    Task<ApiResponse<object>> VerifyEmailAsync(VerifyEmailDto dto);

    /// <summary>
    /// Authenticates user with email/password. Handles lockout, captcha, and liveness pending for drivers.
    /// Returns a partial token for drivers and a token pair for passengers.
    /// </summary>
    Task<ApiResponse<AuthResponseDto>> LoginAsync(LoginRequestDto dto);

    /// <summary>
    /// Refreshes an access token using the httpOnly refresh cookie. Rotates refresh token.
    /// Uses the session boundary persisted from the access token paired with the refresh token.
    /// </summary>
    Task<ApiResponse<AuthResponseDto>> RefreshTokenAsync(string refreshToken);

    /// <summary>
    /// Revokes the current refresh token and clears the cookie.
    /// </summary>
    Task<ApiResponse<object>> LogoutAsync(Guid userId, string refreshToken);

    /// <summary>
    /// Sends a password reset link for passenger and driver accounts.
    /// </summary>
    Task<ApiResponse<object>> ForgotPasswordAsync(ForgotPasswordDto dto);

    /// <summary>
    /// Resets a passenger or driver password using the emailed link token.
    /// </summary>
    Task<ApiResponse<object>> ResetPasswordAsync(ResetPasswordDto dto);

    /// <summary>
    /// Sends an admin-only short-lived password reset code without disclosing account existence.
    /// </summary>
    Task<ApiResponse<object>> ForgotAdminPasswordAsync(ForgotPasswordDto dto);

    /// <summary>
    /// Resets an admin password using the email code and the account's TOTP factor.
    /// </summary>
    Task<ApiResponse<object>> ResetAdminPasswordAsync(AdminResetPasswordDto dto);

    /// <summary>
    /// Returns the Google OAuth authorization URL with PKCE code_challenge.
    /// </summary>
    Task<ApiResponse<object>> GetGoogleAuthUrlAsync(string? role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles the Google OAuth callback. Exchanges code for tokens, creates or logs in user.
    /// </summary>
    Task<ApiResponse<AuthResponseDto>> HandleGoogleCallbackAsync(string code, string state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Admin login step 1: validates email/password, returns partial token with adminStep1 claim.
    /// </summary>
    Task<ApiResponse<object>> AdminLoginAsync(AdminLoginDto dto);

    /// <summary>
    /// Admin login step 2: validates TOTP code against stored secret, issues full admin JWT (10 min, no refresh).
    /// </summary>
    Task<ApiResponse<AuthResponseDto>> AdminTotpVerifyAsync(TotpVerifyDto dto);
}
