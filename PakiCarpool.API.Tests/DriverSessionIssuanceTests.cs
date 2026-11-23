using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PakiCarpool.API.Controllers;
using PakiCarpool.API.Helpers;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Auth;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.DTOs.Driver;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Helpers;
using PakiCarpool.Services.Implementations;
using Xunit;

namespace PakiCarpool.API.Tests;

public sealed class DriverSessionIssuanceTests
{
    private static readonly string[] Prompts = ["Blink", "TurnLeft", "TurnRight"];

    [Fact]
    public async Task PasswordLogin_ThenLivenessApproval_EndsWithFullSessionAndRefreshCookie()
    {
        var fixture = new DriverSessionFixture();

        var login = await fixture.AuthService.LoginAsync(new LoginRequestDto
        {
            Email = fixture.User.Email,
            Password = DriverSessionFixture.Password
        });

        AssertPendingLogin(login);
        Assert.Empty(fixture.PersistedRefreshTokens);

        var completed = await CompleteLivenessAsync(fixture);

        AssertCompletedSession(fixture, completed);
    }

    [Fact]
    public async Task GoogleLogin_ThenLivenessApproval_EndsWithFullSessionAndRefreshCookie()
    {
        var fixture = new DriverSessionFixture();

        var login = await fixture.AuthService.HandleGoogleCallbackAsync("oauth-code", "oauth-state");

        AssertPendingLogin(login);
        Assert.Empty(fixture.PersistedRefreshTokens);
        fixture.OAuthStates.Verify(
            store => store.ConsumeAsync("oauth-state", It.IsAny<CancellationToken>()),
            Times.Once);

        var completed = await CompleteLivenessAsync(fixture);

        AssertCompletedSession(fixture, completed);
    }

    [Fact]
    public async Task LivenessApproval_EndsWithFullSessionAndRefreshCookie()
    {
        var fixture = new DriverSessionFixture();

        var completed = await CompleteLivenessAsync(fixture);

        AssertCompletedSession(fixture, completed);
    }

    private static void AssertPendingLogin(ApiResponse<AuthResponseDto> response)
    {
        Assert.True(response.IsSuccess);
        Assert.NotNull(response.Data);
        Assert.True(response.Data.LivenessPending);
        Assert.Null(response.Data.RawRefreshToken);
        Assert.Null(response.Data.SessionIssuedAt);
        Assert.False(string.IsNullOrWhiteSpace(response.Data.AccessToken));
    }

    private static async Task<CompletedLiveness> CompleteLivenessAsync(DriverSessionFixture fixture)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("userId", fixture.User.Id.ToString())],
                authenticationType: "Test"))
        };
        var controller = new LivenessController(
            fixture.LivenessService,
            NullLogger<LivenessController>.Instance,
            fixture.Configuration,
            fixture.Environment.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var actionResult = await controller.SubmitLiveness(ValidSubmission(fixture.Nonce), CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var response = Assert.IsType<LivenessSubmissionResponseDto>(okResult.Value);
        var setCookie = Assert.Single(httpContext.Response.Headers.SetCookie);
        return new CompletedLiveness(response, setCookie!.ToString());
    }

    private static void AssertCompletedSession(DriverSessionFixture fixture, CompletedLiveness completed)
    {
        var response = completed.Response;
        Assert.True(response.Success);
        Assert.Equal("Approved", response.Status);
        Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));
        Assert.NotNull(response.SessionIssuedAt);
        Assert.NotNull(response.RawRefreshToken);

        var principal = fixture.JwtHelper.ValidateToken(response.AccessToken);
        Assert.NotNull(principal);
        Assert.Equal(fixture.User.Id.ToString(), principal.FindFirstValue("userId"));
        Assert.Equal(RoleNames.Driver, principal.FindFirstValue(ClaimTypes.Role));
        Assert.Null(principal.FindFirst(JwtHelper.LivenessPendingClaim));

        var issuedAtValue = principal.FindFirstValue(JwtRegisteredClaimNames.Iat);
        Assert.True(long.TryParse(issuedAtValue, out var issuedAtUnix));
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtUnix).UtcDateTime;
        Assert.Equal(issuedAt, response.SessionIssuedAt);
        Assert.Equal(DateTimeKind.Utc, response.SessionIssuedAt.Value.Kind);

        var storedToken = Assert.Single(fixture.PersistedRefreshTokens);
        Assert.Equal(fixture.User.Id, storedToken.UserId);
        Assert.False(storedToken.IsRevoked);
        Assert.True(storedToken.ExpiresAt > DateTime.UtcNow);
        Assert.Equal(HashRefreshToken(response.RawRefreshToken), storedToken.Token);
        Assert.Equal(response.SessionIssuedAt, storedToken.CreatedAt);

        Assert.Contains($"{RefreshTokenCookie.Name}={response.RawRefreshToken}", completed.SetCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", completed.SetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", completed.SetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"path={RefreshTokenCookie.Path}", completed.SetCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(JwtHelper.LivenessPendingClaim, response.AccessToken, StringComparison.Ordinal);
    }

    private static string HashRefreshToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static LivenessResultDto ValidSubmission(string nonce)
        => new()
        {
            Nonce = nonce,
            SelfieBase64 = Convert.ToBase64String([0xFF, 0xD8, 0xFF, 0x00]),
            PromptResults = Prompts.Select(prompt => new LivenessPromptResultDto
            {
                Prompt = prompt,
                Frames = ValidFrames(prompt)
            }).ToList()
        };

    private static List<LivenessFrameDto> ValidFrames(string prompt)
        => prompt switch
        {
            "Blink" =>
            [
                new LivenessFrameDto { LeftEyeOpenProbability = 0.1, RightEyeOpenProbability = 0.1 },
                new LivenessFrameDto { LeftEyeOpenProbability = 0.9, RightEyeOpenProbability = 0.9 },
                new LivenessFrameDto { LeftEyeOpenProbability = 0.9, RightEyeOpenProbability = 0.9 }
            ],
            "TurnLeft" =>
            [
                new LivenessFrameDto { HeadEulerAngleY = -30 },
                new LivenessFrameDto { HeadEulerAngleY = -30 }
            ],
            "TurnRight" =>
            [
                new LivenessFrameDto { HeadEulerAngleY = 30 },
                new LivenessFrameDto { HeadEulerAngleY = 30 }
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(prompt))
        };

    private sealed record CompletedLiveness(LivenessSubmissionResponseDto Response, string SetCookie);

    private sealed class DriverSessionFixture
    {
        public const string Password = "Driver-password-123!";

        public DriverSessionFixture()
        {
            User = new User
            {
                Id = Guid.NewGuid(),
                FullName = "Session Test Driver",
                Email = "driver-session@example.com",
                PasswordHash = BCryptHelper.HashPassword(Password),
                Role = UserRole.Driver,
                GoogleId = "google-driver-subject",
                IsEmailVerified = true,
                IsActive = true,
                IsSuspended = false
            };
            Profile = new DriverProfile { Id = Guid.NewGuid(), UserId = User.Id };
            Configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JWT_SECRET"] = new string('s', 64),
                    ["JWT_ISSUER"] = "session-tests",
                    ["JWT_AUDIENCE"] = "session-test-clients",
                    ["AES_KEY_V1"] = Convert.ToBase64String(new byte[32]),
                    ["Auth:DriverTokenExpiryMinutes"] = "15",
                    ["Auth:PassengerTokenExpiryMinutes"] = "15",
                    ["Auth:RefreshTokenExpiryDays"] = "30",
                    ["Auth:LivenessPendingTokenExpiryMinutes"] = "1440",
                    ["Liveness:NonceTtlMinutes"] = "2",
                    ["GOOGLE_CLIENT_ID"] = "google-test-client",
                    ["GOOGLE_CLIENT_SECRET"] = "google-test-secret",
                    ["APP_DOMAIN"] = "app.example.test"
                })
                .Build();
            JwtHelper = new JwtHelper(Configuration);

            Users.Setup(repository => repository.GetByIdAsync(User.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(User);
            Users.Setup(repository => repository.GetByEmailAsync(User.Email, It.IsAny<CancellationToken>()))
                .ReturnsAsync(User);
            Users.Setup(repository => repository.GetByGoogleIdAsync(User.GoogleId!, It.IsAny<CancellationToken>()))
                .ReturnsAsync(User);
            Users.Setup(repository => repository.ResetLoginFailAsync(User.Id, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Users.Setup(repository => repository.GetDriverProfileByUserIdAsync(User.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Profile);
            Users.Setup(repository => repository.UpdateDriverProfileAsync(Profile, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Users.Setup(repository => repository.CreateRefreshTokenAsync(
                    It.IsAny<RefreshToken>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RefreshToken, CancellationToken>((token, _) => PersistedRefreshTokens.Add(token))
                .Returns(Task.CompletedTask);

            var challenge = new LivenessChallenge
            {
                DriverId = User.Id,
                Nonce = Nonce,
                PromptsJson = JsonSerializer.Serialize(Prompts),
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(2)
            };
            Challenges.Setup(repository => repository.GetChallengeByNonceAsync(Nonce, It.IsAny<CancellationToken>()))
                .ReturnsAsync(challenge);
            Challenges.Setup(repository => repository.TryConsumeChallengeAsync(
                    Nonce,
                    User.Id,
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            Storage.Setup(service => service.UploadDriverSelfieAsync(
                    User.Id,
                    It.IsAny<byte[]>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync($"drivers/{User.Id}/profile.jpg");
            OAuthStates.Setup(store => store.ConsumeAsync("oauth-state", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OAuthStateRecord("Driver", "test-verifier", DateTime.UtcNow.AddMinutes(10)));
            HttpClientFactory.Setup(factory => factory.CreateClient("GoogleOAuth"))
                .Returns(new HttpClient(new GoogleTokenHandler()));
            Environment.SetupGet(environment => environment.EnvironmentName).Returns("Development");

            var sessionService = new UserSessionService(Users.Object, JwtHelper, Configuration);
            AuthService = new TestAuthService(
                Users.Object,
                sessionService,
                Rides.Object,
                Email.Object,
                JwtHelper,
                new AesEncryptionHelper(Configuration),
                OAuthStates.Object,
                HttpClientFactory.Object,
                Configuration,
                Captcha.Object);
            LivenessService = new LivenessService(
                Users.Object,
                Challenges.Object,
                Storage.Object,
                Notifications.Object,
                sessionService,
                Configuration,
                HubContext.Object);
        }

        public string Nonce { get; } = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        public User User { get; }
        public DriverProfile Profile { get; }
        public IConfiguration Configuration { get; }
        public JwtHelper JwtHelper { get; }
        public AuthService AuthService { get; }
        public LivenessService LivenessService { get; }
        public List<RefreshToken> PersistedRefreshTokens { get; } = [];
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IRideRepository> Rides { get; } = new();
        public Mock<IEmailService> Email { get; } = new();
        public Mock<IOAuthStateStore> OAuthStates { get; } = new();
        public Mock<IHttpClientFactory> HttpClientFactory { get; } = new();
        public Mock<IHCaptchaService> Captcha { get; } = new();
        public Mock<ILivenessChallengeRepository> Challenges { get; } = new();
        public Mock<IStorageService> Storage { get; } = new();
        public Mock<INotificationService> Notifications { get; } = new();
        public Mock<IHubContext<Hub>> HubContext { get; } = new();
        public Mock<IWebHostEnvironment> Environment { get; } = new();
    }

    private sealed class TestAuthService : AuthService
    {
        public TestAuthService(
            IUserRepository userRepository,
            IUserSessionService userSessionService,
            IRideRepository rideRepository,
            IEmailService emailService,
            JwtHelper jwtHelper,
            AesEncryptionHelper aesHelper,
            IOAuthStateStore oauthStateStore,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IHCaptchaService hCaptchaService)
            : base(
                userRepository,
                userSessionService,
                rideRepository,
                emailService,
                jwtHelper,
                aesHelper,
                oauthStateStore,
                httpClientFactory,
                configuration,
                hCaptchaService,
                NullLogger<AuthService>.Instance)
        {
        }

        protected override Task<GoogleJsonWebSignature.Payload> ValidateGoogleTokenAsync(
            string idToken,
            string clientId,
            CancellationToken cancellationToken)
        {
            Assert.Equal("verified-test-id-token", idToken);
            Assert.Equal("google-test-client", clientId);
            return Task.FromResult(new GoogleJsonWebSignature.Payload
            {
                Subject = "google-driver-subject",
                Email = "driver-session@example.com",
                EmailVerified = true,
                Name = "Session Test Driver"
            });
        }
    }

    private sealed class GoogleTokenHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { id_token = "verified-test-id-token" }),
                    Encoding.UTF8,
                    "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
