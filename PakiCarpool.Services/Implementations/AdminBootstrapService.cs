using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Auth;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Helpers;

namespace PakiCarpool.Services.Implementations;

public sealed class AdminBootstrapService : IAdminBootstrapService
{
    private const string TotpIssuer = "PakiCarpool";
    private readonly IUserRepository _userRepository;
    private readonly IConfiguration _configuration;

    public AdminBootstrapService(IUserRepository userRepository, IConfiguration configuration)
    {
        _userRepository = userRepository;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public async Task<ApiResponse<AdminBootstrapResponseDto>> BootstrapAsync(
        AdminBootstrapRequestDto dto,
        string? seedToken,
        CancellationToken cancellationToken = default)
    {
        var configuredSeedToken = _configuration["ADMIN_SEED_TOKEN"]
                                  ?? Environment.GetEnvironmentVariable("ADMIN_SEED_TOKEN");
        if (string.IsNullOrWhiteSpace(configuredSeedToken)
            || !SeedTokenMatches(configuredSeedToken, seedToken))
        {
            return ApiResponse<AdminBootstrapResponseDto>.Failure(
                ErrorCodes.Unauthorized,
                "Admin bootstrap is not authorized.");
        }

        if (await _userRepository.CountByRoleAsync(UserRole.Admin) > 0)
            return BootstrapUnavailable();

        if (await _userRepository.EmailExistsAsync(dto.Email))
        {
            return ApiResponse<AdminBootstrapResponseDto>.Failure(
                ErrorCodes.ValidationError,
                "An account with this email already exists.");
        }

        var totpSecret = TotpHelper.GenerateSecret();
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = dto.FullName.Trim(),
            Email = dto.Email.Trim().ToLowerInvariant(),
            PasswordHash = BCryptHelper.HashPassword(dto.Password),
            Role = UserRole.Admin,
            IsEmailVerified = true,
            IsActive = true,
            IsSuspended = false,
            TrustScore = AppConstants.TrustScoreInitial,
            CreatedAt = DateTime.UtcNow
        };
        var totp = new TotpSecret
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Secret = totpSecret,
            IsVerified = true
        };

        if (!await _userRepository.CreateFirstAdminAsync(user, totp, cancellationToken))
            return BootstrapUnavailable();

        var encodedIssuer = Uri.EscapeDataString(TotpIssuer);
        var encodedLabel = Uri.EscapeDataString($"{TotpIssuer}:{user.Email}");
        var response = new AdminBootstrapResponseDto
        {
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            TotpSecret = totpSecret,
            OtpAuthUri = $"otpauth://totp/{encodedLabel}?secret={totpSecret}&issuer={encodedIssuer}&algorithm=SHA1&digits=6&period=30"
        };

        return ApiResponse<AdminBootstrapResponseDto>.Success(
            response,
            "First admin created. Store the TOTP enrollment details now; they will not be returned again.");
    }

    private static bool SeedTokenMatches(string configuredSeedToken, string? suppliedSeedToken)
    {
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(configuredSeedToken));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedSeedToken ?? string.Empty));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }

    private static ApiResponse<AdminBootstrapResponseDto> BootstrapUnavailable()
        => ApiResponse<AdminBootstrapResponseDto>.Failure(
            ErrorCodes.NotFound,
            "Admin bootstrap is unavailable.");
}
