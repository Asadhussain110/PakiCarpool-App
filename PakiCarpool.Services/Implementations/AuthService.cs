using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Auth;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Helpers;

namespace PakiCarpool.Services.Implementations;

/// <summary>
/// Implements all authentication flows:
/// email/password registration, OTP verification, login with lockout,
/// JWT/refresh-token management, Google OAuth 2.0 (PKCE), and admin 2-step login.
/// No stack traces to client — all errors are returned as structured ApiResponse.
/// </summary>
public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IUserSessionService _userSessionService;
    private readonly IRideRepository _rideRepository;
    private readonly IEmailService _emailService;
    private readonly JwtHelper _jwtHelper;
    private readonly AesEncryptionHelper _aesHelper;
    private readonly IOAuthStateStore _oauthStateStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IHCaptchaService _hCaptchaService;
    private readonly ILogger<AuthService> _logger;

    private const int AdminResetCodeExpiryMinutes = 10;
    private const int AdminResetCodeAttemptLimit = 5;
    private const int AdminResetCodeRequestLimit = 3;
    private static readonly TimeSpan AdminResetCodeRequestWindow = TimeSpan.FromMinutes(15);
    private const int PasswordResetCodeExpiryMinutes = 10;
    private const int PasswordResetCodeAttemptLimit = 5;
    private const int PasswordResetCodeRequestLimit = 3;
    private static readonly TimeSpan PasswordResetCodeRequestWindow = TimeSpan.FromMinutes(15);
    private const int EmailOtpExpiryMinutesDefault = 10;
    private const int EmailOtpAttemptLimitDefault = 3;
    private const int EmailOtpLockoutMinutesDefault = 15;
    private const int EmailOtpResendCooldownSecondsDefault = 60;
    private const int EmailOtpResendLimitDefault = 3;
    private static readonly TimeSpan EmailOtpResendWindowDefault = TimeSpan.FromMinutes(15);

    public AuthService(
        IUserRepository userRepository,
        IUserSessionService userSessionService,
        IRideRepository rideRepository,
        IEmailService emailService,
        JwtHelper jwtHelper,
        AesEncryptionHelper aesHelper,
        IOAuthStateStore oauthStateStore,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IHCaptchaService hCaptchaService,
        ILogger<AuthService> logger,
        TimeProvider? timeProvider = null)
    {
        _userRepository = userRepository;
        _userSessionService = userSessionService;
        _rideRepository = rideRepository;
        _emailService = emailService;
        _jwtHelper = jwtHelper;
        _aesHelper = aesHelper;
        _oauthStateStore = oauthStateStore;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _hCaptchaService = hCaptchaService;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private readonly TimeProvider _timeProvider;

    private int EmailOtpAttemptLimit =>
        _configuration.GetValue("Auth:EmailOtpAttemptLimit", EmailOtpAttemptLimitDefault);

    private int EmailOtpLockoutMinutes =>
        _configuration.GetValue("Auth:EmailOtpLockoutMinutes", EmailOtpLockoutMinutesDefault);

    private TimeSpan EmailOtpResendCooldown =>
        TimeSpan.FromSeconds(
            _configuration.GetValue("Auth:EmailOtpResendCooldownSeconds", EmailOtpResendCooldownSecondsDefault));

    private int EmailOtpResendLimit =>
        _configuration.GetValue("Auth:EmailOtpResendLimit", EmailOtpResendLimitDefault);

    private TimeSpan EmailOtpResendWindow =>
        TimeSpan.FromMinutes(
            _configuration.GetValue("Auth:EmailOtpResendWindowMinutes", (int)EmailOtpResendWindowDefault.TotalMinutes));

    // ──────────────────────────────────────────────────────────────────────────
    // REGISTRATION
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a new Passenger or Driver with email/password.
    /// Sends a 6-digit OTP. Creates DriverProfile if role is Driver.
    /// Admin registration is forbidden here — admins are seeded only.
    /// </summary>
    public async Task<ApiResponse<object>> RegisterAsync(RegisterRequestDto dto)
    {
        // Admin self-registration is forbidden
        if (dto.Role == UserRole.Admin)
        {
            return new ApiResponse<object>
            {
                Message = "Admin accounts cannot be created through public registration."
            };
        }

        var passwordMessage = PasswordPolicy.GetValidationMessage(dto.Password);
        if (passwordMessage is not null)
        {
            return ApiResponse<object>.Failure(ErrorCodes.ValidationError, passwordMessage);
        }

        if (await _userRepository.EmailExistsAsync(dto.Email))
        {
            return new ApiResponse<object>
            {
                Message = "An account with this email already exists."
            };
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var otp = GenerateOtp();
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = dto.FullName.Trim(),
            Email = dto.Email.Trim().ToLowerInvariant(),
            PasswordHash = BCryptHelper.HashPassword(dto.Password),
            Role = dto.Role,
            IsEmailVerified = false,
            EmailOtp = HashOtp(otp),
            EmailOtpExpiry = now.AddMinutes(
                _configuration.GetValue("Auth:EmailOtpExpiryMinutes", EmailOtpExpiryMinutesDefault)),
            EmailOtpAttempts = 0,
            // Records when the initial code was sent so the resend cooldown applies to it;
            // the initial registration code itself is never counted as a resend.
            EmailOtpLastSentAt = now,
            EmailOtpResendCount = 0,
            IsActive = true,
            IsSuspended = false,
            TrustScore = AppConstants.TrustScoreInitial,
            FailedLoginCount = 0,
            CreatedAt = DateTime.UtcNow
        };

        var driverProfile = dto.Role == UserRole.Driver
            ? new DriverProfile
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                LivenessVerified = false,
                LivenessFailCount = 0,
                AverageRating = 0m
            }
            : null;

        User createdUser;
        try
        {
            createdUser = await _userRepository.CreateWithDriverProfileAsync(user, driverProfile);
        }
        catch (DbUpdateException exception) when (IsDuplicateEmailViolation(exception))
        {
            return DuplicateEmail<object>();
        }

        // SMTP failure must not strand an unverified account with no deliverable code and a
        // blocked email. The smaller and safer recovery is a clean rollback: the just-created
        // unverified user (and its driver profile) is deleted so registering again with the
        // same email works. The user then retries registration rather than depending on a
        // resend for an account whose code email never arrived.
        try
        {
            await _emailService.SendEmailOtpAsync(createdUser.Email, createdUser.FullName, otp);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Verification email delivery failed during registration for user {UserId} ({ExceptionType}).",
                createdUser.Id,
                exception.GetType().Name);
            try
            {
                await _userRepository.DeleteUnverifiedUserAsync(createdUser.Id);
            }
            catch (Exception rollbackException)
            {
                _logger.LogError(
                    "Rollback of unverified user {UserId} failed after email delivery failure ({ExceptionType}).",
                    createdUser.Id,
                    rollbackException.GetType().Name);
            }

            return ApiResponse<object>.Failure(
                ErrorCodes.InternalError,
                "Registration could not be completed. Please try again.");
        }

        return ApiResponse<object>.SuccessMessage("Check your email for the verification code.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // EMAIL OTP VERIFICATION
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the 6-digit OTP. Sets IsEmailVerified = true and clears OTP fields on success.
    /// Wrong codes are counted per account; reaching the configured attempt limit locks
    /// verification for the cooldown and invalidates the current code. Driver accounts
    /// receive a liveness-pending partial token after verification.
    /// </summary>
    public async Task<ApiResponse<object>> VerifyEmailAsync(VerifyEmailDto dto)
    {
        var user = await _userRepository.GetByEmailAsync(dto.Email);
        if (user is null)
            return new ApiResponse<object> { Message = "Account not found." };

        if (IsLoginBanned(user))
            return CreateAccountLocked<object>(user.LoginBannedUntil!.Value);

        if (user.IsEmailVerified)
            return ApiResponse<object>.SuccessMessage("Email is already verified.");

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // While the account is OTP-locked, correct and wrong codes get the same generic
        // response — no remaining lock time is revealed to an unverified caller.
        if (user.EmailOtpLockedUntil is not null && user.EmailOtpLockedUntil.Value > now)
            return EmailOtpLocked<object>();

        if (user.EmailOtp is null || user.EmailOtpExpiry is null)
            return new ApiResponse<object> { Message = "No pending OTP found. Please request a new one." };

        if (now > user.EmailOtpExpiry)
            return new ApiResponse<object> { Message = "OTP has expired. Please request a new one." };

        var incomingHash = HashOtp(dto.Otp);
        if (!FixedTimeHashEquals(incomingHash, user.EmailOtp))
        {
            var lockoutUntil = now.AddMinutes(EmailOtpLockoutMinutes);
            await _userRepository.IncrementEmailOtpAttemptsAsync(
                user.Id, now, EmailOtpAttemptLimit, lockoutUntil);

            return user.EmailOtpAttempts + 1 >= EmailOtpAttemptLimit
                ? EmailOtpLocked<object>()
                : new ApiResponse<object> { Message = "Invalid verification code." };
        }

        await ResetLoginFailuresAsync(user);
        user.IsEmailVerified = true;
        user.EmailOtp = null;
        user.EmailOtpExpiry = null;
        // A successful verification resets the per-account OTP attempt counter.
        user.EmailOtpAttempts = 0;
        user.EmailOtpLockedUntil = null;
        user.EmailOtpLastSentAt = null;
        user.EmailOtpResendCount = 0;
        user.EmailOtpResendWindowStart = null;
        await _userRepository.UpdateAsync(user);

        if (user.Role == UserRole.Driver)
        {
            var partialToken = _jwtHelper.GeneratePartialToken(
                user.Id, user.Email, user.Role.ToString(), JwtHelper.LivenessPendingClaim);

            return ApiResponse<object>.Success(new AuthResponseDto
            {
                AccessToken = partialToken,
                UserId = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role,
                LivenessPending = true
            }, "Liveness verification required.");
        }

        return ApiResponse<object>.SuccessMessage("Email verified successfully.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // EMAIL OTP RESEND
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a replacement email-verification OTP. The per-account resend cooldown,
    /// per-window resend limit, and OTP lock are enforced atomically per account, and
    /// issuing the new code invalidates the previous one. The response — success flag,
    /// status, and message — is identical whether or not the email belongs to a
    /// registered, unverified, unlocked account, so account existence is never revealed.
    /// </summary>
    public async Task<ApiResponse<object>> ResendEmailOtpAsync(ResendEmailVerificationDto dto)
    {
        const string responseMessage = "If an account exists for this email, a new verification code has been sent.";
        var user = await _userRepository.GetByEmailAsync(dto.Email);
        if (user is null)
            return ApiResponse<object>.SuccessMessage(responseMessage);

        // Verified, admin, and inactive accounts get the same response as unknown emails.
        if (user.Role == UserRole.Admin || user.IsEmailVerified || !user.IsActive)
            return ApiResponse<object>.SuccessMessage(responseMessage);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var otp = GenerateOtp();
        var issued = await _userRepository.TryIssueEmailOtpAsync(
            user.Id,
            HashOtp(otp),
            now,
            now.AddMinutes(
                _configuration.GetValue("Auth:EmailOtpExpiryMinutes", EmailOtpExpiryMinutesDefault)),
            EmailOtpResendCooldown,
            EmailOtpResendWindow,
            EmailOtpResendLimit);
        if (!issued)
            // Locked, cooling down, or over the window limit — same generic response.
            return ApiResponse<object>.SuccessMessage(responseMessage);

        try
        {
            await _emailService.SendEmailOtpAsync(user.Email, user.FullName, otp);
        }
        catch (Exception exception)
        {
            // Delivery failure is logged without ever logging the code. The response stays
            // identical so account existence is not revealed through error behavior; the
            // stored code remains valid and the user can retry after the cooldown.
            _logger.LogError(
                "Verification email resend delivery failed for user {UserId} ({ExceptionType}).",
                user.Id,
                exception.GetType().Name);
        }

        return ApiResponse<object>.SuccessMessage(responseMessage);
    }

    private static ApiResponse<T> EmailOtpLocked<T>()
        => ApiResponse<T>.Failure(
            ErrorCodes.ValidationError,
            "Too many invalid verification attempts. Try again later.");

    // ──────────────────────────────────────────────────────────────────────────
    // EMAIL/PASSWORD LOGIN
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Authenticates with email/password. Handles lockout, captcha threshold, and liveness
    /// pending for drivers. Returns a partial token for drivers and a token pair for passengers.
    /// </summary>
    public async Task<ApiResponse<AuthResponseDto>> LoginAsync(LoginRequestDto dto)
    {
        var user = await _userRepository.GetByEmailAsync(dto.Email);
        if (user is null)
            // No account row exists to lock; enumeration protection is preserved and the response stays generic.
            return InvalidCredentials<AuthResponseDto>();

        if (IsLoginBanned(user))
            return CreateAccountLocked<AuthResponseDto>(user.LoginBannedUntil!.Value);

        // hCaptcha gate — enforced when the stored failure count already meets the threshold.
        // This fires BEFORE password validation so brute-forcers can't bypass it by always
        // sending a request; the gate applies even when the upcoming password check would succeed.
        if (user.LoginFailCount >= AppConstants.CaptchaRequiredAfterAttempts)
        {
            var captchaValid = false;
            if (!string.IsNullOrWhiteSpace(dto.HCaptchaToken))
            {
                try
                {
                    captchaValid = await _hCaptchaService.VerifyAsync(dto.HCaptchaToken);
                }
                catch
                {
                }
            }

            if (!captchaValid)
            {
                return await RecordCredentialFailureAsync<AuthResponseDto>(user);
            }
        }

        // Password validation
        if (!BCryptHelper.VerifyPassword(dto.Password, user.PasswordHash ?? string.Empty))
            return await RecordCredentialFailureAsync<AuthResponseDto>(user);

        // Successful password check — reset counter
        await ResetLoginFailuresAsync(user);

        if (!user.IsEmailVerified || !user.IsActive || user.IsSuspended)
            return InvalidCredentials<AuthResponseDto>();

        // Drivers must pass liveness before full access
        if (user.Role == UserRole.Driver)
            return await IssueDriverLivenessPendingAsync(user);

        // Issue full token pair
        return await IssueTokenPairAsync(user);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // REFRESH TOKEN
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rotates refresh token. Old token is revoked; new pair issued.
    /// Validates token not revoked and not expired.
    /// </summary>
    public async Task<ApiResponse<AuthResponseDto>> RefreshTokenAsync(string refreshToken)
    {
        var hashedToken = HashRefreshToken(refreshToken);
        var stored = await _userRepository.GetRefreshTokenAsync(hashedToken);

        if (stored is null || stored.IsRevoked || stored.ExpiresAt < DateTime.UtcNow)
            return new ApiResponse<AuthResponseDto> { Message = ErrorCodes.TokenExpired };

        var user = await _userRepository.GetByIdAsync(stored.UserId);
        if (user is null || !user.IsActive || user.IsSuspended)
            return new ApiResponse<AuthResponseDto> { Message = ErrorCodes.Unauthorized };

        if (user.Role == UserRole.Admin)
            return new ApiResponse<AuthResponseDto> { Message = ErrorCodes.Unauthorized };

        if (user.Role == UserRole.Driver)
        {
            var hasActiveRide = await _rideRepository.HasActiveRideForUserAsync(user.Id);

            if (!hasActiveRide)
            {
                var mostRecentRide = await _rideRepository.GetMostRecentRideForUserAsync(user.Id);

                if (mostRecentRide?.Status == RideStatus.Completed
                    && mostRecentRide.CompletedAt.HasValue
                    && mostRecentRide.CompletedAt.Value >= stored.SessionIssuedAt)
                {
                    return new ApiResponse<AuthResponseDto> { Message = ErrorCodes.LivenessRequired };
                }
            }
        }

        if (!await _userRepository.TryConsumeRefreshTokenAsync(hashedToken, DateTime.UtcNow))
            return new ApiResponse<AuthResponseDto> { Message = ErrorCodes.TokenExpired };

        return await IssueTokenPairAsync(user);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // LOGOUT
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Revokes the provided refresh token, ending the session.
    /// </summary>
    public async Task<ApiResponse<object>> LogoutAsync(Guid userId, string refreshToken)
    {
        var hashedToken = HashRefreshToken(refreshToken);
        var stored = await _userRepository.GetRefreshTokenAsync(hashedToken);

        if (stored is not null && stored.UserId == userId)
        {
            stored.IsRevoked = true;
            await _userRepository.UpdateRefreshTokenAsync(stored);
        }

        return ApiResponse<object>.SuccessMessage("Logged out successfully.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // FORGOT / RESET PASSWORD
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a short-lived password reset code and sends it by email.
    /// Always returns 200 to prevent email enumeration.
    /// </summary>
    public async Task<ApiResponse<object>> ForgotPasswordAsync(ForgotPasswordDto dto)
    {
        const string responseMessage = "If this email is registered, password reset instructions have been sent.";
        var user = await _userRepository.GetByEmailAsync(dto.Email);
        if (user is null)
            // Deliberately vague — do not reveal if email is registered
            return ApiResponse<object>.SuccessMessage(responseMessage);

        // Admins use the separate code-and-TOTP reset flow.
        if (user.Role == UserRole.Admin)
            return ApiResponse<object>.SuccessMessage(responseMessage);

        var now = DateTime.UtcNow;
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var codeHash = HashAdminResetCode(code);
        var issued = await _userRepository.TryIssuePasswordResetCodeAsync(
            user.Id,
            codeHash,
            now,
            now.AddMinutes(_configuration.GetValue(
                "Auth:PasswordResetCodeExpiryMinutes",
                PasswordResetCodeExpiryMinutes)),
            PasswordResetCodeRequestWindow,
            PasswordResetCodeRequestLimit);
        if (issued)
            await _emailService.SendPasswordResetEmailAsync(user.Email, user.FullName, code);

        return ApiResponse<object>.SuccessMessage(responseMessage);
    }

    /// <summary>
    /// Resets a passenger or driver's password using the emailed code.
    /// </summary>
    public async Task<ApiResponse<object>> ResetPasswordAsync(ResetPasswordDto dto)
    {
        const string invalidMessage = "The reset code is invalid or expired. Request a new code and try again.";

        var passwordMessage = PasswordPolicy.GetValidationMessage(dto.NewPassword);
        if (passwordMessage is not null)
        {
            return ApiResponse<object>.Failure(ErrorCodes.ValidationError, passwordMessage);
        }

        var user = await _userRepository.GetByEmailAsync(dto.Email);
        if (user is null || user.Role == UserRole.Admin || !user.IsActive ||
            user.PasswordResetToken is null ||
            user.PasswordResetTokenExpiry is null ||
            user.PasswordResetTokenExpiry <= DateTime.UtcNow ||
            user.PasswordResetAttempts >= PasswordResetCodeAttemptLimit)
        {
            return new ApiResponse<object> { Message = invalidMessage };
        }

        var expectedHash = HashAdminResetCode(dto.Code);
        if (!FixedTimeHashEquals(expectedHash, user.PasswordResetToken))
        {
            await _userRepository.IncrementPasswordResetAttemptsAsync(
                user.Id, DateTime.UtcNow, PasswordResetCodeAttemptLimit);
            return new ApiResponse<object> { Message = invalidMessage };
        }

        var completed = await _userRepository.CompletePasswordResetAsync(
            user.Id,
            expectedHash,
            DateTime.UtcNow,
            BCryptHelper.HashPassword(dto.NewPassword),
            PasswordResetCodeAttemptLimit);
        if (!completed)
            return new ApiResponse<object> { Message = invalidMessage };

        return ApiResponse<object>.SuccessMessage("Password reset successfully. Please log in.");
    }

    public async Task<ApiResponse<object>> ForgotAdminPasswordAsync(ForgotPasswordDto dto)
    {
        const string responseMessage = "If this email is registered, a password reset code has been sent.";
        var user = await _userRepository.GetByEmailAsync(dto.Email);
        if (user is null || user.Role != UserRole.Admin || !user.IsActive)
            return ApiResponse<object>.SuccessMessage(responseMessage);

        var totpSecret = await _userRepository.GetTotpSecretAsync(user.Id);
        if (totpSecret is null || !totpSecret.IsVerified)
            return ApiResponse<object>.SuccessMessage(responseMessage);

        var now = DateTime.UtcNow;
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var codeHash = HashAdminResetCode(code);
        var issued = await _userRepository.TryIssueAdminPasswordResetCodeAsync(
            user.Id,
            codeHash,
            now,
            now.AddMinutes(AdminResetCodeExpiryMinutes),
            AdminResetCodeRequestWindow,
            AdminResetCodeRequestLimit);
        if (!issued)
            return ApiResponse<object>.SuccessMessage(responseMessage);

        try
        {
            await _emailService.SendAdminPasswordResetCodeAsync(user.Email, user.FullName, code);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Admin password reset code email delivery failed for user {UserId} ({ExceptionType}).",
                user.Id,
                exception.GetType().Name);
        }

        return ApiResponse<object>.SuccessMessage(responseMessage);
    }

    public async Task<ApiResponse<object>> ResetAdminPasswordAsync(AdminResetPasswordDto dto)
    {
        const string invalidMessage = "The reset request is invalid or expired. Request a new code and try again.";

        var passwordMessage = PasswordPolicy.GetValidationMessage(dto.NewPassword);
        if (passwordMessage is not null)
        {
            return ApiResponse<object>.Failure(ErrorCodes.ValidationError, passwordMessage);
        }

        var user = await _userRepository.GetByEmailAsync(dto.Email);
        if (user is null || user.Role != UserRole.Admin || !user.IsActive ||
            user.AdminPasswordResetCodeHash is null ||
            user.AdminPasswordResetCodeExpiry is null ||
            user.AdminPasswordResetCodeExpiry <= DateTime.UtcNow ||
            user.AdminPasswordResetAttempts >= AdminResetCodeAttemptLimit)
        {
            return new ApiResponse<object> { Message = invalidMessage };
        }

        var expectedHash = HashAdminResetCode(dto.Code);
        if (!FixedTimeHashEquals(expectedHash, user.AdminPasswordResetCodeHash))
        {
            await _userRepository.IncrementAdminPasswordResetAttemptsAsync(
                user.Id, DateTime.UtcNow, AdminResetCodeAttemptLimit);
            return new ApiResponse<object> { Message = invalidMessage };
        }

        var totpSecret = await _userRepository.GetTotpSecretAsync(user.Id);
        if (totpSecret is null || !totpSecret.IsVerified ||
            !TotpHelper.VerifyCode(totpSecret.Secret, dto.TotpCode))
        {
            await _userRepository.IncrementAdminPasswordResetAttemptsAsync(
                user.Id, DateTime.UtcNow, AdminResetCodeAttemptLimit);
            return new ApiResponse<object> { Message = invalidMessage };
        }

        var now = DateTime.UtcNow;
        var completed = await _userRepository.CompleteAdminPasswordResetAsync(
            user.Id,
            expectedHash,
            now,
            BCryptHelper.HashPassword(dto.NewPassword),
            AdminResetCodeAttemptLimit);
        if (!completed)
            return new ApiResponse<object> { Message = invalidMessage };

        return ApiResponse<object>.SuccessMessage("Password reset successfully. Sign in with your new password.");
    }

    private string HashAdminResetCode(string code)
    {
        var secret = _configuration["JWT_SECRET"]
            ?? Environment.GetEnvironmentVariable("JWT_SECRET")
            ?? _configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("JWT secret is not configured.");

        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedTimeHashEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(left),
                Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GOOGLE OAUTH 2.0 (PKCE)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the Google OAuth 2.0 authorization URL with PKCE code_challenge.
    /// Role is encoded in the state parameter for new user creation.
    /// </summary>
    public async Task<ApiResponse<object>> GetGoogleAuthUrlAsync(string? role, CancellationToken cancellationToken = default)
    {
        var clientId = _configuration["GOOGLE_CLIENT_ID"]
            ?? throw new InvalidOperationException("GOOGLE_CLIENT_ID is not configured.");
        var appDomain = _configuration["APP_DOMAIN"] ?? "app.pakicarpool.pk";
        var redirectUri = $"https://{appDomain}/auth/google/callback";

        var codeVerifier = GenerateSecureToken(32);
        var codeChallenge = ComputeCodeChallenge(codeVerifier);
        var state = GenerateSecureToken(32);
        if (!await _oauthStateStore.TryCreateAsync(
            state,
            role ?? "Passenger",
            codeVerifier,
            TimeSpan.FromMinutes(10),
            cancellationToken))
        {
            return ApiResponse<object>.Failure(ErrorCodes.InternalError, "Unable to start Google sign-in.");
        }

        var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth" +
                      $"?client_id={Uri.EscapeDataString(clientId)}" +
                      $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                      $"&response_type=code" +
                      $"&scope=openid%20email%20profile" +
                      $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                      $"&code_challenge_method=S256" +
                      $"&state={Uri.EscapeDataString(state)}";

        return ApiResponse<object>.Success(new { authUrl, state }, "Google OAuth URL generated.");
    }

    /// <summary>
    /// Handles the Google OAuth callback. Validates the ID token, creates or logs in the user.
    /// </summary>
    public async Task<ApiResponse<AuthResponseDto>> HandleGoogleCallbackAsync(
        string code,
        string state,
        CancellationToken cancellationToken = default)
    {
        var clientId = _configuration["GOOGLE_CLIENT_ID"]
            ?? throw new InvalidOperationException("GOOGLE_CLIENT_ID is not configured.");
        var clientSecret = _configuration["GOOGLE_CLIENT_SECRET"]
            ?? throw new InvalidOperationException("GOOGLE_CLIENT_SECRET is not configured.");
        var appDomain = _configuration["APP_DOMAIN"] ?? "app.pakicarpool.pk";
        var redirectUri = $"https://{appDomain}/auth/google/callback";

        var storedState = await _oauthStateStore.ConsumeAsync(state, cancellationToken);
        if (storedState is null)
        {
            return InvalidCredentials<AuthResponseDto>();
        }

        var roleStr = storedState.Role;
        var codeVerifier = storedState.CodeVerifier;

        // Exchange code for tokens using Google OAuth endpoint
        string idToken;
        try
        {
            var httpClient = _httpClientFactory.CreateClient("GoogleOAuth");
            using var tokenResponse = await httpClient.PostAsync("https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["code"] = code,
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["redirect_uri"] = redirectUri,
                    ["grant_type"] = "authorization_code",
                    ["code_verifier"] = codeVerifier
                }), cancellationToken);

            if (!tokenResponse.IsSuccessStatusCode)
                return InvalidCredentials<AuthResponseDto>();

            var tokenContent = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            using var tokenDoc = JsonDocument.Parse(tokenContent);
            idToken = tokenDoc.RootElement.GetProperty("id_token").GetString()
                      ?? throw new InvalidOperationException("id_token missing from Google response.");
        }
        catch
        {
            if (cancellationToken.IsCancellationRequested)
                throw;

            return InvalidCredentials<AuthResponseDto>();
        }

        // Validate ID token using Google.Apis.Auth
        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await ValidateGoogleTokenAsync(idToken, clientId, cancellationToken);
        }
        catch
        {
            if (cancellationToken.IsCancellationRequested)
                throw;

            return InvalidCredentials<AuthResponseDto>();
        }

        if (!payload.EmailVerified)
            return new ApiResponse<AuthResponseDto> { Message = "Google account email is not verified." };

        if (string.IsNullOrWhiteSpace(payload.Subject) || string.IsNullOrWhiteSpace(payload.Email))
            return InvalidCredentials<AuthResponseDto>();

        var googleId = payload.Subject;
        var email = payload.Email.ToLowerInvariant();
        var fullName = payload.Name ?? payload.GivenName ?? "Google User";

        // Find existing user by GoogleId or Email
        var existingUser = await _userRepository.GetByGoogleIdAsync(googleId, cancellationToken)
                   ?? await _userRepository.GetByEmailAsync(email, cancellationToken);

        if (existingUser is not null)
        {
            if (IsLoginBanned(existingUser))
                return CreateAccountLocked<AuthResponseDto>(existingUser.LoginBannedUntil!.Value);

            // Link GoogleId if missing
            if (string.IsNullOrEmpty(existingUser.GoogleId))
            {
                existingUser.GoogleId = googleId;
                await _userRepository.UpdateAsync(existingUser, cancellationToken);
            }

            if (!existingUser.IsActive || existingUser.IsSuspended)
                return InvalidCredentials<AuthResponseDto>();

            // Drivers must pass liveness at every login, including Google OAuth
            if (existingUser.Role == UserRole.Driver)
                return await IssueDriverLivenessPendingAsync(existingUser, cancellationToken);

            return await IssueTokenPairAsync(existingUser, cancellationToken);
        }

        // Create new user
        if (!Enum.TryParse<UserRole>(roleStr, out var role) || role == UserRole.Admin)
            role = UserRole.Passenger;

        var newUser = new User
        {
            Id = Guid.NewGuid(),
            FullName = fullName,
            Email = email,
            PasswordHash = null!, // Google OAuth users have no password
            Role = role,
            GoogleId = googleId,
            IsEmailVerified = true, // Google already verified the email
            IsActive = true,
            IsSuspended = false,
            TrustScore = AppConstants.TrustScoreInitial,
            FailedLoginCount = 0,
            CreatedAt = DateTime.UtcNow
        };

        var googleDriverProfile = role == UserRole.Driver
            ? new DriverProfile
            {
                Id = Guid.NewGuid(),
                UserId = newUser.Id,
                LivenessVerified = false,
                LivenessFailCount = 0,
                AverageRating = 0m
            }
            : null;
        User createdUser;
        try
        {
            createdUser = await _userRepository.CreateWithDriverProfileAsync(
                newUser, googleDriverProfile, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateEmailViolation(exception))
        {
            return DuplicateEmail<AuthResponseDto>();
        }

        if (role == UserRole.Driver)
            return await IssueDriverLivenessPendingAsync(createdUser, cancellationToken);

        return await IssueTokenPairAsync(createdUser, cancellationToken);
    }

    /// <summary>
    /// Validates Google's signed identity token. Kept virtual so OAuth session tests can supply a verified payload.
    /// </summary>
    protected virtual async Task<GoogleJsonWebSignature.Payload> ValidateGoogleTokenAsync(
        string idToken,
        string clientId,
        CancellationToken cancellationToken)
    {
        var validationSettings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = new[] { clientId }
        };
        return await GoogleJsonWebSignature.ValidateAsync(idToken, validationSettings)
            .WaitAsync(cancellationToken);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ADMIN 2-STEP LOGIN
    // ──────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Admin login step 1: validates email/password, returns partial token with adminStep1 claim.
    /// Admins are seeded — no public registration.
    /// </summary>
    public async Task<ApiResponse<object>> AdminLoginAsync(AdminLoginDto dto)
    {
        var user = await _userRepository.GetByEmailAsync(dto.Email);

        if (user is null)
            return InvalidCredentials<object>();

        if (IsLoginBanned(user))
            return CreateAccountLocked<object>(user.LoginBannedUntil!.Value);

        if (user.Role != UserRole.Admin)
            return await RecordCredentialFailureAsync<object>(user);

        if (!BCryptHelper.VerifyPassword(dto.Password, user.PasswordHash ?? string.Empty) || !user.IsActive)
            return await RecordCredentialFailureAsync<object>(user);

        await ResetLoginFailuresAsync(user);

        var partialToken = _jwtHelper.GeneratePartialToken(
            user.Id,
            user.Email,
            RoleNames.Admin,
            "adminStep1",
            authTokenVersion: user.AuthTokenVersion);

        return ApiResponse<object>.Success(
            new { partialToken },
            "Step 1 complete. Please verify your TOTP code.");
    }

    /// <summary>
    /// Admin login step 2: validates TOTP code, issues full admin JWT (10 min, no refresh token).
    /// </summary>
    public async Task<ApiResponse<AuthResponseDto>> AdminTotpVerifyAsync(TotpVerifyDto dto)
    {
        var principal = _jwtHelper.ValidateToken(dto.PartialToken);
        if (principal is null)
            return new ApiResponse<AuthResponseDto> { Message = "Invalid or expired partial token." };

        var hasAdminStep1 = principal.FindFirst("adminStep1")?.Value == "true";
        if (!hasAdminStep1)
            return new ApiResponse<AuthResponseDto> { Message = "Invalid partial token type." };

        var userIdClaim = principal.FindFirst("userId")?.Value
                          ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
            return new ApiResponse<AuthResponseDto> { Message = "Invalid token claims." };

        var tokenVersionValue = principal.FindFirst("authTokenVersion")?.Value ?? "0";
        if (!int.TryParse(
                tokenVersionValue,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var tokenVersion) ||
            await _userRepository.GetAuthTokenVersionAsync(userId) != tokenVersion)
        {
            return new ApiResponse<AuthResponseDto> { Message = "Invalid or expired partial token." };
        }

        var totpSecret = await _userRepository.GetTotpSecretAsync(userId);
        if (totpSecret is null || !totpSecret.IsVerified)
            return new ApiResponse<AuthResponseDto> { Message = "TOTP not configured for this admin account." };

        if (!TotpHelper.VerifyCode(totpSecret.Secret, dto.Code))
            return new ApiResponse<AuthResponseDto> { Message = "Invalid TOTP code." };

        var user = await _userRepository.GetByIdAsync(userId);
        if (user is null || user.Role != UserRole.Admin)
            return new ApiResponse<AuthResponseDto> { Message = "User not found." };

        // Admin token: 10 min, NO refresh token
        var authTokenVersion = await _userRepository.GetAuthTokenVersionAsync(user.Id) ?? 0;
        var adminToken = _jwtHelper.GenerateAdminToken(user.Id, user.Email, authTokenVersion);

        return ApiResponse<AuthResponseDto>.Success(new AuthResponseDto
        {
            AccessToken = adminToken,
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role,
            LivenessPending = false
        }, "Admin login successful.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Issues a short-lived partial JWT requiring liveness before full driver access.
    /// </summary>
    private async Task<ApiResponse<AuthResponseDto>> IssueDriverLivenessPendingAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        var driverProfile = await _userRepository.GetDriverProfileByUserIdAsync(user.Id, cancellationToken);
        if (driverProfile is not null &&
            driverProfile.LivenessBannedUntil.HasValue &&
            driverProfile.LivenessBannedUntil > DateTime.UtcNow)
        {
            return new ApiResponse<AuthResponseDto>
            {
                Message = $"{ErrorCodes.LivenessBanned}:{driverProfile.LivenessBannedUntil:O}"
            };
        }

        if (driverProfile?.LivenessVerified == true)
        {
            driverProfile.LivenessVerified = false;
            driverProfile.PhotoUpdatedAt = null;
            await _userRepository.UpdateDriverProfileAsync(driverProfile, cancellationToken);
        }

        var partialToken = _jwtHelper.GeneratePartialToken(
            user.Id,
            user.Email,
            user.Role.ToString(),
            JwtHelper.LivenessPendingClaim,
            _configuration.GetValue("Auth:LivenessPendingTokenExpiryMinutes", 24 * 60));

        return ApiResponse<AuthResponseDto>.Success(new AuthResponseDto
        {
            AccessToken = partialToken,
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role,
            LivenessPending = true
        }, "Liveness verification required.");
    }

    private bool IsLoginBanned(User user)
    {
        return user.LoginBannedUntil.HasValue && user.LoginBannedUntil.Value > DateTime.UtcNow;
    }

    private async Task ResetLoginFailuresAsync(User user)
    {
        await _userRepository.ResetLoginFailAsync(user.Id);
        user.LoginFailCount = 0;
        user.LoginBannedUntil = null;
    }

    private async Task<ApiResponse<T>> RecordLoginFailureAsync<T>(User user)
    {
        await _userRepository.IncrementLoginFailAsync(user.Id);
        var failCount = await _userRepository.GetLoginFailCountAsync(user.Id);
        if (!failCount.HasValue)
            return InvalidCredentials<T>();

        user.LoginFailCount = failCount.Value;
        var threshold = _configuration.GetValue<int>("Auth:LoginLockoutThreshold");
        if (failCount.Value < threshold)
            return InvalidCredentials<T>();

        var lockoutMinutes = _configuration.GetValue<int>("Auth:LoginLockoutMinutes");
        var bannedUntil = DateTime.UtcNow.AddMinutes(lockoutMinutes);
        await _userRepository.SetLoginBanAsync(user.Id, bannedUntil);
        user.LoginFailCount = 0;
        user.LoginBannedUntil = bannedUntil;
        return CreateAccountLocked<T>(bannedUntil);
    }

    private async Task<ApiResponse<T>> RecordCredentialFailureAsync<T>(User? user)
    {
        if (user is null)
            return InvalidCredentials<T>();

        return await RecordLoginFailureAsync<T>(user);
    }

    private static bool IsDuplicateEmailViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_Users_Email" or "IX_Users_Email_Lower"
        };

    private static ApiResponse<T> DuplicateEmail<T>()
        => ApiResponse<T>.Failure(
            ErrorCodes.ValidationError,
            "An account with this email already exists.");

    private static ApiResponse<T> InvalidCredentials<T>()
    {
        return ApiResponse<T>.Failure(ErrorCodes.Unauthorized, "Invalid credentials.");
    }

    private static ApiResponse<T> CreateAccountLocked<T>(DateTime bannedUntil)
    {
        return ApiResponse<T>.Failure(
            ErrorCodes.AccountLocked,
            "Too many failed login attempts. Try again later.",
            bannedUntil);
    }

    private async Task<ApiResponse<AuthResponseDto>> IssueTokenPairAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        if (user.Role == UserRole.Admin)
            return new ApiResponse<AuthResponseDto> { Message = ErrorCodes.Unauthorized };

        var session = await _userSessionService.IssueAsync(user, cancellationToken);
        return ApiResponse<AuthResponseDto>.Success(session, "Login successful.");
    }

    private static string GenerateOtp()
    {
        var bytes = new byte[4];
        RandomNumberGenerator.Fill(bytes);
        var number = BitConverter.ToUInt32(bytes, 0) % 1_000_000;
        return number.ToString("D6");
    }

    private static string GenerateSecureToken(int byteLength = 32)
    {
        var bytes = new byte[byteLength];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string HashOtp(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string HashRefreshToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeCodeChallenge(string codeVerifier)
    {
        var bytes = Encoding.ASCII.GetBytes(codeVerifier);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}
