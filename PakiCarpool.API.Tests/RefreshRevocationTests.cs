using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PakiCarpool.API.Controllers;
using PakiCarpool.API.Helpers;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Helpers;
using PakiCarpool.Services.Implementations;
using Xunit;

namespace PakiCarpool.API.Tests;

public sealed class RefreshRevocationTests
{
    [Fact]
    public async Task ConcurrentDoubleRefresh_ProducesOnlyOneSession()
    {
        var fixture = new RefreshFixture(UserRole.Passenger);
        fixture.BlockTokenConsumptionUntilBothRequestsArrive();

        var responses = await Task.WhenAll(
            fixture.AuthService.RefreshTokenAsync(RefreshFixture.RawRefreshToken),
            fixture.AuthService.RefreshTokenAsync(RefreshFixture.RawRefreshToken));

        var successful = Assert.Single(responses, response => response.IsSuccess);
        var rejected = Assert.Single(responses, response => !response.IsSuccess);
        Assert.Equal(ErrorCodes.TokenExpired, rejected.Message);
        var replacement = Assert.Single(fixture.PersistedRefreshTokens);
        Assert.Equal(successful.Data!.SessionIssuedAt, replacement.SessionIssuedAt);
        Assert.NotEqual(fixture.StoredToken.SessionIssuedAt, replacement.SessionIssuedAt);
        fixture.Users.Verify(
            repository => repository.TryConsumeRefreshTokenAsync(
                fixture.HashedRefreshToken,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ReusedRefreshToken_IsRejectedWithoutIssuingAnotherSession()
    {
        var fixture = new RefreshFixture(UserRole.Passenger);

        var first = await fixture.AuthService.RefreshTokenAsync(RefreshFixture.RawRefreshToken);
        var replay = await fixture.AuthService.RefreshTokenAsync(RefreshFixture.RawRefreshToken);

        Assert.True(first.IsSuccess);
        Assert.False(replay.IsSuccess);
        Assert.Equal(ErrorCodes.TokenExpired, replay.Message);
        Assert.Single(fixture.PersistedRefreshTokens);
        fixture.Users.Verify(
            repository => repository.TryConsumeRefreshTokenAsync(
                fixture.HashedRefreshToken,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CookieOnlyRefresh_RequiresLivenessAfterRideCompletedWithinSession()
    {
        var fixture = new RefreshFixture(UserRole.Driver);
        var now = DateTime.UtcNow;
        fixture.StoredToken.CreatedAt = now;
        fixture.StoredToken.SessionIssuedAt = now.AddHours(-1);
        fixture.Rides.Setup(repository => repository.HasActiveRideForUserAsync(fixture.User.Id))
            .ReturnsAsync(false);
        fixture.Rides.Setup(repository => repository.GetMostRecentRideForUserAsync(fixture.User.Id))
            .ReturnsAsync(new Ride
            {
                Status = RideStatus.Completed,
                CompletedAt = now.AddMinutes(-30)
            });

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie =
            $"{RefreshTokenCookie.Name}={RefreshFixture.RawRefreshToken}";
        var controller = new AuthController(
            fixture.AuthService,
            fixture.Configuration,
            fixture.Environment.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var result = await controller.Refresh();

        var unauthorized = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        var error = Assert.IsType<ErrorResponse>(unauthorized.Value);
        Assert.Equal(ErrorCodes.LivenessRequired, error.Error);
        Assert.False(httpContext.Request.Headers.ContainsKey("Authorization"));
        fixture.Users.Verify(
            repository => repository.TryConsumeRefreshTokenAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Empty(fixture.PersistedRefreshTokens);
    }

    private sealed class RefreshFixture
    {
        public const string RawRefreshToken = "single-use-refresh-token";
        private int _consumeCount;
        private int _consumeArrivalCount;
        private TaskCompletionSource? _bothTokenConsumptionsArrived;

        public RefreshFixture(UserRole role)
        {
            User = new User
            {
                Id = Guid.NewGuid(),
                FullName = "Refresh Test User",
                Email = "refresh-test@example.com",
                Role = role,
                IsActive = true,
                IsSuspended = false
            };
            var issuedAt = DateTime.UtcNow.AddHours(-1);
            StoredToken = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = User.Id,
                Token = HashRefreshToken(RawRefreshToken),
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = issuedAt,
                SessionIssuedAt = issuedAt
            };
            Configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JWT_SECRET"] = new string('r', 64),
                    ["JWT_ISSUER"] = "refresh-tests",
                    ["JWT_AUDIENCE"] = "refresh-test-clients",
                    ["AES_KEY_V1"] = Convert.ToBase64String(new byte[32]),
                    ["Auth:PassengerTokenExpiryMinutes"] = "30",
                    ["Auth:DriverTokenExpiryMinutes"] = "15",
                    ["Auth:RefreshTokenExpiryDays"] = "7"
                })
                .Build();
            var jwtHelper = new JwtHelper(Configuration);

            Users.Setup(repository => repository.GetRefreshTokenAsync(HashedRefreshToken))
                .ReturnsAsync(() => StoredToken);
            Users.Setup(repository => repository.GetByIdAsync(User.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(User);
            Users.Setup(repository => repository.TryConsumeRefreshTokenAsync(
                    HashedRefreshToken,
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    if (_bothTokenConsumptionsArrived is not null)
                    {
                        if (Interlocked.Increment(ref _consumeArrivalCount) == 2)
                            _bothTokenConsumptionsArrived.TrySetResult();

                        await _bothTokenConsumptionsArrived.Task;
                    }

                    if (Interlocked.CompareExchange(ref _consumeCount, 1, 0) != 0)
                        return false;

                    StoredToken.IsRevoked = true;
                    return true;
                });
            Users.Setup(repository => repository.CreateRefreshTokenAsync(
                    It.IsAny<RefreshToken>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RefreshToken, CancellationToken>((token, _) => PersistedRefreshTokens.Add(token))
                .Returns(Task.CompletedTask);
            Environment.SetupGet(environment => environment.EnvironmentName).Returns("Development");

            var sessionService = new UserSessionService(Users.Object, jwtHelper, Configuration);
            AuthService = new AuthService(
                Users.Object,
                sessionService,
                Rides.Object,
                Email.Object,
                jwtHelper,
                new AesEncryptionHelper(Configuration),
                OAuthStates.Object,
                HttpClientFactory.Object,
                Configuration,
                Captcha.Object,
                NullLogger<AuthService>.Instance);
        }

        public User User { get; }
        public RefreshToken StoredToken { get; }
        public string HashedRefreshToken => HashRefreshToken(RawRefreshToken);
        public IConfiguration Configuration { get; }
        public AuthService AuthService { get; }
        public ConcurrentBag<RefreshToken> PersistedRefreshTokens { get; } = [];
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IRideRepository> Rides { get; } = new();
        public Mock<IEmailService> Email { get; } = new();
        public Mock<IOAuthStateStore> OAuthStates { get; } = new();
        public Mock<IHttpClientFactory> HttpClientFactory { get; } = new();
        public Mock<IHCaptchaService> Captcha { get; } = new();
        public Mock<IWebHostEnvironment> Environment { get; } = new();

        public void BlockTokenConsumptionUntilBothRequestsArrive()
        {
            _bothTokenConsumptionsArrived = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static string HashRefreshToken(string token)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }
}
