using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using PakiCarpool.Domain.DTOs.Auth;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Helpers;

namespace PakiCarpool.Services.Implementations;

/// <summary>
/// Creates a full access token and its corresponding persisted refresh token.
/// </summary>
public sealed class UserSessionService : IUserSessionService
{
    private readonly IUserRepository _userRepository;
    private readonly JwtHelper _jwtHelper;
    private readonly IConfiguration _configuration;

    public UserSessionService(
        IUserRepository userRepository,
        JwtHelper jwtHelper,
        IConfiguration configuration)
    {
        _userRepository = userRepository;
        _jwtHelper = jwtHelper;
        _configuration = configuration;
    }

    public async Task<AuthResponseDto> IssueAsync(User user, CancellationToken cancellationToken = default)
    {
        if (user.Role == UserRole.Admin)
            throw new InvalidOperationException("Admin accounts do not receive refresh-token sessions.");

        var accessToken = _jwtHelper.GenerateAccessToken(
            user.Id,
            user.Email,
            user.Role.ToString(),
            out var sessionIssuedAt);
        var rawRefreshToken = GenerateSecureToken(64);
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = HashRefreshToken(rawRefreshToken),
            ExpiresAt = sessionIssuedAt.AddDays(_configuration.GetValue<int>("Auth:RefreshTokenExpiryDays")),
            IsRevoked = false,
            CreatedAt = sessionIssuedAt,
            SessionIssuedAt = sessionIssuedAt
        };

        await _userRepository.CreateRefreshTokenAsync(refreshToken, cancellationToken);

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role,
            LivenessPending = false,
            SessionIssuedAt = sessionIssuedAt,
            RawRefreshToken = rawRefreshToken
        };
    }

    private static string GenerateSecureToken(int byteLength)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string HashRefreshToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
