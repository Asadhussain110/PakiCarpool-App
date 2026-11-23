using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using PakiCarpool.API.Helpers;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Auth;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.API.Controllers;

/// <summary>
/// Authentication endpoints: register, login, OAuth, refresh, admin 2FA.
/// Base route: /api/v1/auth
/// </summary>
[Route("api/v1/auth")]
public sealed class AuthController : ApiControllerBase
{
    private readonly IAuthService _authService;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;

    public AuthController(
        IAuthService authService,
        IConfiguration configuration,
        IWebHostEnvironment env)
    {
        _authService = authService;
        _configuration = configuration;
        _env = env;
    }

    /// <summary>POST /auth/register — Register passenger or driver.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto dto)
    {
        if (!ModelState.IsValid)
            return InvalidModel();

        return FromResponse(await _authService.RegisterAsync(dto));
    }

    /// <summary>POST /auth/verify-email — Verify email OTP.</summary>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailDto dto)
    {
        if (!ModelState.IsValid)
            return InvalidModel();

        return FromResponse(await _authService.VerifyEmailAsync(dto));
    }

    /// <summary>POST /auth/login — Email/password login.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
    {
        if (!ModelState.IsValid)
            return InvalidModel();

        var response = await _authService.LoginAsync(dto);
        if (response.IsSuccess && response.Data?.RawRefreshToken is not null)
            SetRefreshTokenCookie(response.Data.RawRefreshToken);

        return FromResponse(response);
    }

    /// <summary>POST /auth/refresh — Rotate refresh token from httpOnly cookie.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh()
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenCookie.Name, out var refreshToken)
            || string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized(ErrorResponse.Create(
                StatusCodes.Status401Unauthorized,
                ErrorCodes.Unauthorized,
                "Refresh token cookie is missing."));
        }

        var response = await _authService.RefreshTokenAsync(refreshToken);
        if (response.IsSuccess && response.Data?.RawRefreshToken is not null)
            SetRefreshTokenCookie(response.Data.RawRefreshToken);

        return FromResponse(response);
    }

    /// <summary>POST /auth/logout — Revoke refresh token and clear cookie.</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout()
    {
        Request.Cookies.TryGetValue(RefreshTokenCookie.Name, out var refreshToken);
        var response = await _authService.LogoutAsync(GetUserId(), refreshToken ?? string.Empty);
        ClearRefreshTokenCookie();
        return FromResponse(response);
    }

    /// <summary>POST /auth/forgot-password — Send password reset email.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        if (!ModelState.IsValid)
            return InvalidModel();

        return FromResponse(await _authService.ForgotPasswordAsync(dto));
    }

    /// <summary>POST /auth/reset-password — Reset password with emailed token.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        if (!ModelState.IsValid)
            return InvalidModel();

        return FromResponse(await _authService.ResetPasswordAsync(dto));
    }

    /// <summary>POST /auth/admin/forgot-password — Send a short-lived admin reset code.</summary>
    [HttpPost("admin/forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotAdminPassword([FromBody] ForgotPasswordDto dto)
    {
        if (!ModelState.IsValid)
            return InvalidModel();

        return FromResponse(await _authService.ForgotAdminPasswordAsync(dto));
    }

    /// <summary>POST /auth/admin/reset-password — Reset an admin password with code and TOTP.</summary>
    [HttpPost("admin/reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetAdminPassword([FromBody] AdminResetPasswordDto dto)
    {
        if (!ModelState.IsValid)
            return InvalidModel();

        return FromResponse(await _authService.ResetAdminPasswordAsync(dto));
    }

    /// <summary>GET /auth/google — Start Google OAuth PKCE flow.</summary>
    [HttpGet("google")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GoogleAuth([FromQuery] string? role, CancellationToken cancellationToken)
    {
        return FromResponse(await _authService.GetGoogleAuthUrlAsync(role, cancellationToken));
    }

    /// <summary>GET /auth/google/callback — OAuth callback handler.</summary>
    [HttpGet("google/callback")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GoogleCallback(
        [FromQuery] string code,
        [FromQuery] string state,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return BadRequest(ErrorResponse.Create(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationError,
                "OAuth code and state are required."));
        }

        var response = await _authService.HandleGoogleCallbackAsync(code, state, cancellationToken);
        if (response.IsSuccess && response.Data?.RawRefreshToken is not null)
            SetRefreshTokenCookie(response.Data.RawRefreshToken);

        return FromResponse(response);
    }

    /// <summary>POST /auth/admin/login — Admin step 1 (email/password).</summary>
    [HttpPost("admin/login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AdminLogin([FromBody] AdminLoginDto dto)
    {
        if (!ModelState.IsValid)
            return InvalidModel();

        return FromResponse(await _authService.AdminLoginAsync(dto));
    }

    /// <summary>POST /auth/admin/totp-verify — Admin step 2 (TOTP).</summary>
    [HttpPost("admin/totp-verify")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AdminTotpVerify([FromBody] TotpVerifyDto dto)
    {
        if (!ModelState.IsValid)
            return InvalidModel();

        return FromResponse(await _authService.AdminTotpVerifyAsync(dto));
    }

    private void SetRefreshTokenCookie(string rawRefreshToken)
        => RefreshTokenCookie.Append(
            Response,
            rawRefreshToken,
            _configuration.GetValue<int>("Auth:RefreshTokenExpiryDays"),
            !_env.IsDevelopment());

    private void ClearRefreshTokenCookie()
        => RefreshTokenCookie.Delete(Response, !_env.IsDevelopment());

    private IActionResult InvalidModel()
    {
        var errors = ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage);

        return BadRequest(ErrorResponse.Create(
            StatusCodes.Status400BadRequest,
            ErrorCodes.ValidationError,
            string.Join(" ", errors)));
    }
}
