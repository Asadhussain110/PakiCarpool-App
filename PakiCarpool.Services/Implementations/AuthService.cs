using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Auth;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Repository.EFCore;
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
    private readonly IEmailService _emailService;
    private readonly JwtHelper _jwtHelper;
    private readonly AesEncryptionHelper _aesHelper;
    private readonly IConfiguration _configuration;
    private readonly IHCaptchaService _hCaptchaService;
    private readonly PakiCarpoolDbContext _dbContext;

    public AuthService(
        IUserRepository userRepository,
        IEmailService emailService,
        JwtHelper jwtHelper,
        AesEncryptionHelper aesHelper,
        IConfiguration configuration,
        IHCaptchaService hCaptchaService,
        PakiCarpoolDbContext dbContext)
    {
        _userRepository = userRepository;
        _emailService = emailService;
        _jwtHelper = jwtHelper;
        _aesHelper = aesHelper;
        _configuration = configuration;
        _hCaptchaService = hCaptchaService;
        _dbContext = dbContext;
    }

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

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                if (await _userRepository.EmailExistsAsync(dto.Email))
                {
                    await transaction.RollbackAsync();
                    return new ApiResponse<object>
                    {
                        Message = "An account with this email already exists."
                    };
                }

                var otp = GenerateOtp();
                var otpHash = HashOtp(otp);

                var user = new User
                {
                    Id = Guid.NewGuid(),
                    FullName = dto.FullName.Trim(),
                    Email = dto.Email.Trim().ToLowerInvariant(),
                    PasswordHash = BCryptHelper.HashPassword(dto.Password),
                    Role = dto.Role,
                    IsEmailVerified = false,
                    EmailOtp = otpHash,
                    EmailOtpExpiry = DateTime.UtcNow.AddMinutes(AppConstants.OtpExpiryMinutes),
                    IsActive = true,
                    IsSuspended = false,
                    TrustScore = AppConstants.TrustScoreInitial,
                    FailedLoginCount = 0,
                    CreatedAt = DateTime.UtcNow
                };

                var createdUser = await _userRepository.CreateAsync(user);

                // Create DriverProfile for drivers
                if (dto.Role == UserRole.Driver)
                {
                    var driverProfile = new DriverProfile
                    {
                        Id = Guid.NewGuid(),
                        UserId = createdUser.Id,
                        LivenessVerified = false,
                        LivenessFailCount = 0,
                        AverageRating = 0m
                    };
                    await _userRepository.CreateDriverProfileAsync(driverProfile);
                }

                await _emailService.SendEmailOtpAsync(createdUser.Email, createdUser.FullName, otp);
                await transaction.CommitAsync();
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync();
                }
                catch
                {
                }

                throw;
            }

            return ApiResponse<object>.SuccessMessage("Check your email for the verification code.");
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // EMAIL OTP VERIFICATION
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the 6-digit OTP. Sets IsEmailVerified = true and clears OTP fields on success.
    /// </summary>
    public async Task<ApiResponse<object>> VerifyEmailAsync(VerifyEmailDto dto)
    {
        var user = await _userRepository.GetByEmailAsync(dto.Email);
        if (user is null)
            return new ApiResponse<object> { Message = "Account not found." };

        if (user.IsEmailVerified)
            return ApiResponse<object>.SuccessMessage("Email is already verified.");

        if (user.EmailOtp is null || user.EmailOtpExpiry is null)
            return new ApiResponse<object> { Message = "No pending OTP found. Please request a new one." };

        if (DateTime.UtcNow > user.EmailOtpExpiry)
            return new ApiResponse<object> { Message = "OTP has expired. Please request a new one." };

        var incomingHash = HashOtp(dto.Otp);
        if (!string.Equals(incomingHash, user.EmailOtp, StringComparison.Ordinal))
            return new ApiResponse<object> { Message = "Invalid verification code." };

        user.IsEmailVerified = true;
        user.EmailOtp = null;
        user.EmailOtpExpiry = null;
        await _userRepository.UpdateAsync(user);

        return ApiResponse<object>.SuccessMessage("Email verified successfully.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // EMAIL/PASSWORD LOGIN
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Authenticates with email/password. Handles lockout, captcha threshold, and liveness
    /// pending for drivers. Returns JWT access token + refresh token via cookie.
    /// </summary>
    public async Task<ApiResponse<AuthResponseDto>> LoginAsync(LoginRequestDto dto)
    {
        var user = await _userRepository.GetByEmailAsync(dto.Email);
        if (user is null)
            return new ApiResponse<AuthResponseDto> { Message = "Invalid email or password." };

        if (!user.IsEmailVerified)
            return new ApiResponse<AuthResponseDto> { Message = "Please verify your email before logging in." };

        if (!user.IsActive)
            return new ApiResponse<AuthResponseDto> { Message = ErrorCodes.AccountSuspended };

        if (user.IsSuspended)
            return new ApiResponse<AuthResponseDto> { Message = ErrorCodes.AccountSuspended };

        // Lockout check
        if (user.LockoutUntil.HasValue && user.LockoutUntil > DateTime.UtcNow)
        {
            var remaining = (int)(user.LockoutUntil.Value - DateTime.UtcNow).TotalSeconds;
            return new ApiResponse<AuthResponseDto>
            {
                Message = $"{ErrorCodes.AccountLocked}:{remaining}"
            };
        }

        // hCaptcha gate — enforced when the stored failure count already meets the threshold.
        // This fires BEFORE password validation so brute-forcers can't bypass it by always
        // sending a request; the gate applies even when the upcoming password check would succeed.
        if (user.FailedLoginCount >= AppConstants.CaptchaRequiredAfterAttempts)
        {
            if (string.IsNullOrWhiteSpace(dto.HCaptchaToken))
                return new ApiResponse<AuthResponseDto> { Message = ErrorCodes.CaptchaRequired };

            var captchaValid = await _hCaptchaService.VerifyAsync(dto.HCaptchaToken);
            if (!captchaValid)
                return new ApiResponse<AuthResponseDto> { Message = ErrorCodes.CaptchaInvalid };
        }

        // Password validation
        if (!BCryptHelper.VerifyPassword(dto.Password, user.PasswordHash ?? string.Empty))
        {
            user.FailedLoginCount++;

            if (user.FailedLoginCount >= AppConstants.MaxLoginAttempts)
            {
                user.LockoutUntil = DateTime.UtcNow.AddMinutes(AppConstants.LockoutDurationMinutes);
                user.FailedLoginCount = 0;
                await _userRepository.UpdateAsync(user);
                return new ApiResponse<AuthResponseDto>
                {
                    Message = $"{ErrorCodes.AccountLocked}:{AppConstants.LockoutDurationMinutes * 60}"
                };
            }

            await _userRepository.UpdateAsync(user);

            // Signal that hCaptcha is required after 3 failures
            var requireCaptcha = user.FailedLoginCount >= AppConstants.CaptchaRequiredAfterAttempts;
            return new ApiResponse<AuthResponseDto>
            {
                Message = requireCaptcha
                    ? "Invalid password. Please complete the captcha challenge."
                    : "Invalid email or password."
            };
        }

        // Successful password check — reset counter
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        await _userRepository.UpdateAsync(user);

        // Drivers must pass liveness before full access
        if (user.Role == UserRole.Driver)
        {
            var driverProfile = await _userRepository.GetDriverProfileByUserIdAsync(user.Id);
            if (driverProfile != null)
            {
                // Check for liveness ban
                if (driverProfile.LivenessBannedUntil.HasValue && driverProfile.LivenessBannedUntil > DateTime.UtcNow)
                {
                    return new ApiResponse<AuthResponseDto>
                    {
                        Message = $"{ErrorCodes.LivenessBanned}:{driverProfile.LivenessBannedUntil:O}"
                    };
                }
            }

            if (driverProfile is null || !driverProfile.LivenessVerified)
            {
                // Return a partial token with livenessPending claim
                var partialToken = _jwtHelper.GeneratePartialToken(
                    user.Id, user.Email, user.Role.ToString(), "livenessPending");

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

            return await IssueTokenPairAsync(user, false);
        }

        // Issue full token pair
        return await IssueTokenPairAsync(user, false);
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

        // Revoke old token
        stored.IsRevoked = true;
        await _userRepository.UpdateRefreshTokenAsync(stored);

        return await IssueTokenPairAsync(user, false);
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
    /// Generates a secure password reset token and sends it by email.
    /// Always returns 200 to prevent email enumeration.
    /// </summary>
    public async Task<ApiResponse<object>> ForgotPasswordAsync(ForgotPasswordDto dto)
    {
        var user = await _userRepository.GetByEmailAsync(dto.Email);
        if (user is null)
            // Deliberately vague — do not reveal if email is registered
            return ApiResponse<object>.SuccessMessage("If this email is registered, a reset link has been sent.");

        var resetToken = GenerateSecureToken();
        var tokenHash = HashOtp(resetToken); // Reuse SHA-256 hash for reset tokens too

        user.EmailOtp = tokenHash; // Repurpose OTP field for reset token (short-lived)
        user.EmailOtpExpiry = DateTime.UtcNow.AddMinutes(30);
        await _userRepository.UpdateAsync(user);

        await _emailService.SendPasswordResetEmailAsync(user.Email, user.FullName, resetToken);

        return ApiResponse<object>.SuccessMessage("If this email is registered, a reset link has been sent.");
    }

    /// <summary>
    /// Resets the user's password using the emailed token.
    /// </summary>
    public async Task<ApiResponse<object>> ResetPasswordAsync(ResetPasswordDto dto)
    {
        var user = await _userRepository.GetByEmailAsync(dto.Email);
        if (user is null)
            return new ApiResponse<object> { Message = "Invalid reset request." };

        if (user.EmailOtp is null || user.EmailOtpExpiry is null)
            return new ApiResponse<object> { Message = "Reset token is invalid or expired." };

        if (DateTime.UtcNow > user.EmailOtpExpiry)
            return new ApiResponse<object> { Message = "Reset token has expired. Please request a new one." };

        var expectedHash = HashOtp(dto.Token);
        if (!string.Equals(expectedHash, user.EmailOtp, StringComparison.Ordinal))
            return new ApiResponse<object> { Message = "Invalid reset token." };

        user.PasswordHash = BCryptHelper.HashPassword(dto.NewPassword);
        user.EmailOtp = null;
        user.EmailOtpExpiry = null;
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        await _userRepository.UpdateAsync(user);

        // Revoke all existing refresh tokens to force re-login everywhere
        await _userRepository.RevokeAllRefreshTokensAsync(user.Id);

        return ApiResponse<object>.SuccessMessage("Password reset successfully. Please log in.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GOOGLE OAUTH 2.0 (PKCE)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the Google OAuth 2.0 authorization URL with PKCE code_challenge.
    /// Role is encoded in the state parameter for new user creation.
    /// </summary>
    public async Task<ApiResponse<object>> GetGoogleAuthUrlAsync(string? role)
    {
        var clientId = _configuration["GOOGLE_CLIENT_ID"]
            ?? throw new InvalidOperationException("GOOGLE_CLIENT_ID is not configured.");
        var appDomain = _configuration["APP_DOMAIN"] ?? "app.pakicarpool.pk";
        var redirectUri = $"https://{appDomain}/auth/google/callback";

        var codeVerifier = GenerateSecureToken(32);
        var codeChallenge = ComputeCodeChallenge(codeVerifier);
        var state = JsonSerializer.Serialize(new { role = role ?? "Passenger", codeVerifier });
        var stateEncoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(state));

        var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth" +
                      $"?client_id={Uri.EscapeDataString(clientId)}" +
                      $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                      $"&response_type=code" +
                      $"&scope=openid%20email%20profile" +
                      $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                      $"&code_challenge_method=S256" +
                      $"&state={Uri.EscapeDataString(stateEncoded)}";

        await Task.CompletedTask;
        return ApiResponse<object>.Success(new { authUrl, stateEncoded }, "Google OAuth URL generated.");
    }

    /// <summary>
    /// Handles the Google OAuth callback. Validates the ID token, creates or logs in the user.
    /// </summary>
    public async Task<ApiResponse<AuthResponseDto>> HandleGoogleCallbackAsync(string code, string state)
    {
        var clientId = _configuration["GOOGLE_CLIENT_ID"]
            ?? throw new InvalidOperationException("GOOGLE_CLIENT_ID is not configured.");
        var clientSecret = _configuration["GOOGLE_CLIENT_SECRET"]
            ?? throw new InvalidOperationException("GOOGLE_CLIENT_SECRET is not configured.");
        var appDomain = _configuration["APP_DOMAIN"] ?? "app.pakicarpool.pk";
        var redirectUri = $"https://{appDomain}/auth/google/callback";

        // Decode state to get role and codeVerifier
        string roleStr = "Passenger";
        string codeVerifier = string.Empty;
        try
        {
            var stateJson = Encoding.UTF8.GetString(Convert.FromBase64String(state));
            using var doc = JsonDocument.Parse(stateJson);
            roleStr = doc.RootElement.GetProperty("role").GetString() ?? "Passenger";
            codeVerifier = doc.RootElement.GetProperty("codeVerifier").GetString() ?? string.Empty;
        }
        catch
        {
            return new ApiResponse<AuthResponseDto> { Message = "Invalid OAuth state parameter." };
        }

        // Exchange code for tokens using Google OAuth endpoint
        string idToken;
        try
        {
            using var httpClient = new HttpClient();
            var tokenResponse = await httpClient.PostAsync("https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["code"] = code,
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["redirect_uri"] = redirectUri,
                    ["grant_type"] = "authorization_code",
                    ["code_verifier"] = codeVerifier
                }));

            var tokenContent = await tokenResponse.Content.ReadAsStringAsync();
            using var tokenDoc = JsonDocument.Parse(tokenContent);
            idToken = tokenDoc.RootElement.GetProperty("id_token").GetString()
                      ?? throw new InvalidOperationException("id_token missing from Google response.");
        }
        catch (Exception ex)
        {
            return new ApiResponse<AuthResponseDto>
            {
                Message = $"Failed to exchange OAuth code: {ex.Message}"
            };
        }

        // Validate ID token using Google.Apis.Auth
        GoogleJsonWebSignature.Payload payload;
        try
        {
            var validationSettings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { clientId }
            };
            payload = await GoogleJsonWebSignature.ValidateAsync(idToken, validationSettings);
        }
        catch
        {
            return new ApiResponse<AuthResponseDto> { Message = "Google ID token validation failed." };
        }

        var googleId = payload.Subject;
        var email = payload.Email.ToLowerInvariant();
        var fullName = payload.Name ?? payload.GivenName ?? "Google User";

        // Find existing user by GoogleId or Email
        var existingUser = await _userRepository.GetByGoogleIdAsync(googleId)
                           ?? await _userRepository.GetByEmailAsync(email);

        if (existingUser is not null)
        {
            // Link GoogleId if missing
            if (string.IsNullOrEmpty(existingUser.GoogleId))
            {
                existingUser.GoogleId = googleId;
                await _userRepository.UpdateAsync(existingUser);
            }

            if (!existingUser.IsActive || existingUser.IsSuspended)
                return new ApiResponse<AuthResponseDto> { Message = ErrorCodes.AccountSuspended };

            // Drivers must pass liveness at every login, including Google OAuth
            if (existingUser.Role == UserRole.Driver)
                return await IssueDriverLivenessPendingAsync(existingUser);

            return await IssueTokenPairAsync(existingUser, false);
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

        var createdUser = await _userRepository.CreateAsync(newUser);

        if (role == UserRole.Driver)
        {
            var driverProfile = new DriverProfile
            {
                Id = Guid.NewGuid(),
                UserId = createdUser.Id,
                LivenessVerified = false,
                LivenessFailCount = 0,
                AverageRating = 0m
            };
            await _userRepository.CreateDriverProfileAsync(driverProfile);
            return await IssueDriverLivenessPendingAsync(createdUser);
        }

        return await IssueTokenPairAsync(createdUser, false);
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

        if (user is null || user.Role != UserRole.Admin)
            return new ApiResponse<object> { Message = "Invalid credentials." };

        if (!user.IsActive)
            return new ApiResponse<object> { Message = ErrorCodes.AccountSuspended };

        if (user.LockoutUntil.HasValue && user.LockoutUntil > DateTime.UtcNow)
            return new ApiResponse<object> { Message = ErrorCodes.AccountLocked };

        if (!BCryptHelper.VerifyPassword(dto.Password, user.PasswordHash ?? string.Empty))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= AppConstants.MaxLoginAttempts)
            {
                user.LockoutUntil = DateTime.UtcNow.AddMinutes(AppConstants.LockoutDurationMinutes);
                user.FailedLoginCount = 0;
            }
            await _userRepository.UpdateAsync(user);
            return new ApiResponse<object> { Message = "Invalid credentials." };
        }

        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        await _userRepository.UpdateAsync(user);

        var partialToken = _jwtHelper.GeneratePartialToken(
            user.Id, user.Email, RoleNames.Admin, "adminStep1");

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

        var totpSecret = await _userRepository.GetTotpSecretAsync(userId);
        if (totpSecret is null || !totpSecret.IsVerified)
            return new ApiResponse<AuthResponseDto> { Message = "TOTP not configured for this admin account." };

        if (!TotpHelper.VerifyCode(totpSecret.Secret, dto.Code))
            return new ApiResponse<AuthResponseDto> { Message = "Invalid TOTP code." };

        var user = await _userRepository.GetByIdAsync(userId);
        if (user is null || user.Role != UserRole.Admin)
            return new ApiResponse<AuthResponseDto> { Message = "User not found." };

        // Admin token: 10 min, NO refresh token
        var adminToken = _jwtHelper.GenerateAdminToken(user.Id, user.Email);

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
    private async Task<ApiResponse<AuthResponseDto>> IssueDriverLivenessPendingAsync(User user)
    {
        var driverProfile = await _userRepository.GetDriverProfileByUserIdAsync(user.Id);
        if (driverProfile is not null &&
            driverProfile.LivenessBannedUntil.HasValue &&
            driverProfile.LivenessBannedUntil > DateTime.UtcNow)
        {
            return new ApiResponse<AuthResponseDto>
            {
                Message = $"{ErrorCodes.LivenessBanned}:{driverProfile.LivenessBannedUntil:O}"
            };
        }

        var partialToken = _jwtHelper.GeneratePartialToken(
            user.Id, user.Email, user.Role.ToString(), "livenessPending");

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

    private async Task<ApiResponse<AuthResponseDto>> IssueTokenPairAsync(User user, bool livenessPending)
    {
        var accessToken = _jwtHelper.GenerateAccessToken(user.Id, user.Email, user.Role.ToString());

        // Create refresh token
        var rawRefreshToken = GenerateSecureToken(64);
        var hashedRefreshToken = HashRefreshToken(rawRefreshToken);

        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = hashedRefreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(AppConstants.RefreshTokenExpiryDays),
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.CreateRefreshTokenAsync(refreshTokenEntity);

        // The raw token is included in the response data so the controller can set the httpOnly cookie.
        return ApiResponse<AuthResponseDto>.Success(
            new AuthResponseDto
            {
                AccessToken = accessToken,
                UserId = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role,
                LivenessPending = livenessPending,
                RawRefreshToken = rawRefreshToken
            },
            "Login successful.");
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
