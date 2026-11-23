using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Moq;
using PakiCarpool.API.Controllers;
using PakiCarpool.Domain.DTOs.Driver;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Helpers;
using PakiCarpool.Services.Implementations;
using Xunit;

namespace PakiCarpool.API.Tests;

public sealed class LivenessServiceTests
{
    private static readonly string[] Prompts = ["Blink", "TurnLeft", "TurnRight"];

    [Fact]
    public async Task IssueChallenge_PersistsAndReturnsServerSelectedPrompts()
    {
        var fixture = new ServiceFixture();
        LivenessChallenge? persisted = null;
        fixture.Challenges
            .Setup(repository => repository.InsertChallengeAsync(It.IsAny<LivenessChallenge>(), It.IsAny<CancellationToken>()))
            .Callback<LivenessChallenge, CancellationToken>((challenge, _) => persisted = challenge)
            .Returns(Task.CompletedTask);

        var result = await fixture.Service.IssueChallengeAsync(fixture.DriverId);

        Assert.NotNull(persisted);
        Assert.Equal(result.Prompts, JsonSerializer.Deserialize<string[]>(persisted.PromptsJson));
        Assert.Equal(3, result.Prompts.Count);
        Assert.Equal(3, result.Prompts.Distinct(StringComparer.Ordinal).Count());
        Assert.All(result.Prompts, prompt => Assert.Contains(prompt, new[] { "Blink", "TurnLeft", "TurnRight", "Smile" }));
        Assert.Equal(persisted.Nonce, result.Nonce);
        Assert.Equal(persisted.ExpiresAt, result.ExpiresAt);
    }

    [Fact]
    public async Task ProcessLivenessResult_ForgedPassedBooleans_AreRejected()
    {
        var fixture = new ServiceFixture();
        var challenge = fixture.AddValidChallenge();
        fixture.Challenges
            .Setup(repository => repository.TryConsumeChallengeAsync(
                challenge.Nonce,
                fixture.DriverId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var dto = JsonSerializer.Deserialize<LivenessResultDto>($$"""
            {
              "nonce": "{{challenge.Nonce}}",
              "selfieBase64": "{{ValidSelfieBase64}}",
              "promptResults": [
                { "prompt": "Blink", "passed": true },
                { "prompt": "TurnLeft", "passed": true },
                { "prompt": "TurnRight", "passed": true }
              ]
            }
            """, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var result = await fixture.Service.ProcessLivenessResultAsync(fixture.DriverId, dto);

        AssertRejected(result);
        Assert.Equal(1, fixture.Profile.LivenessFailCount);
        fixture.Storage.Verify(
            service => service.UploadDriverSelfieAsync(
                It.IsAny<Guid>(),
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessLivenessResult_ReplayedChallenge_IsRejected()
    {
        var fixture = new ServiceFixture();
        var challenge = fixture.AddValidChallenge();
        var consumeCalls = 0;
        fixture.Challenges
            .Setup(repository => repository.TryConsumeChallengeAsync(
                challenge.Nonce,
                fixture.DriverId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref consumeCalls) == 1);
        var dto = ValidSubmission(challenge.Nonce);

        var firstResult = await fixture.Service.ProcessLivenessResultAsync(fixture.DriverId, dto);
        var replayResult = await fixture.Service.ProcessLivenessResultAsync(fixture.DriverId, dto);

        Assert.True(firstResult.Success);
        Assert.Equal("Approved", firstResult.Status);
        Assert.NotNull(firstResult.AccessToken);
        AssertRejected(replayResult);
        fixture.Storage.Verify(
            service => service.UploadDriverSelfieAsync(
                fixture.DriverId,
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessLivenessResult_ExpiredChallenge_IsRejectedWithoutConsuming()
    {
        var fixture = new ServiceFixture();
        var challenge = fixture.AddValidChallenge();
        challenge.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);

        var result = await fixture.Service.ProcessLivenessResultAsync(
            fixture.DriverId,
            ValidSubmission(challenge.Nonce));

        AssertRejected(result);
        fixture.Challenges.Verify(
            repository => repository.TryConsumeChallengeAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Null(result.AccessToken);
    }

    [Fact]
    public async Task ProcessLivenessResult_ChallengeIssuedToAnotherUser_IsRejected()
    {
        var fixture = new ServiceFixture();
        var challenge = fixture.AddValidChallenge();
        challenge.DriverId = Guid.NewGuid();

        var result = await fixture.Service.ProcessLivenessResultAsync(
            fixture.DriverId,
            ValidSubmission(challenge.Nonce));

        AssertRejected(result);
        fixture.Challenges.Verify(
            repository => repository.TryConsumeChallengeAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.False(fixture.Profile.LivenessVerified);
    }

    [Fact]
    public async Task ProcessLivenessResult_MismatchedPromptEvidence_IsRejected()
    {
        var fixture = new ServiceFixture();
        var challenge = fixture.AddValidChallenge();
        fixture.Challenges
            .Setup(repository => repository.TryConsumeChallengeAsync(
                challenge.Nonce,
                fixture.DriverId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var dto = ValidSubmission(challenge.Nonce);
        dto.PromptResults.Reverse();

        var result = await fixture.Service.ProcessLivenessResultAsync(fixture.DriverId, dto);

        AssertRejected(result);
        Assert.Null(result.AccessToken);
        Assert.Equal(1, fixture.Profile.LivenessFailCount);
    }

    [Fact]
    public void AdminController_DoesNotExposeForbiddenManualLivenessApproval()
    {
        var routeTemplates = typeof(AdminController)
            .GetMethods()
            .SelectMany(method => method.GetCustomAttributes(typeof(HttpMethodAttribute), inherit: true)
                .Cast<HttpMethodAttribute>()
                .Select(attribute => attribute.Template)
                .Where(template => template is not null)
                .Select(template => template!))
            .ToArray();

        Assert.DoesNotContain(routeTemplates, template => template.Contains("manual-verify", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(IAdminService).GetMethods(), method => method.Name == "ManualVerifyDriverAsync");
    }

    private static void AssertRejected(LivenessSubmissionResponseDto response)
    {
        Assert.False(response.Success);
        Assert.False(response.PendingReview);
        Assert.Equal("Rejected", response.Status);
        Assert.Null(response.AccessToken);
    }

    private static LivenessResultDto ValidSubmission(string nonce)
        => new()
        {
            Nonce = nonce,
            SelfieBase64 = ValidSelfieBase64,
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
            "Smile" =>
            [
                new LivenessFrameDto { SmilingProbability = 0.9 },
                new LivenessFrameDto { SmilingProbability = 0.9 }
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(prompt))
        };

    private static string ValidSelfieBase64 => Convert.ToBase64String([0xFF, 0xD8, 0xFF, 0x00]);

    private sealed class ServiceFixture
    {
        public ServiceFixture()
        {
            Profile = new DriverProfile { Id = Guid.NewGuid(), UserId = DriverId };
            UserRepository
                .Setup(repository => repository.GetDriverProfileByUserIdAsync(DriverId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Profile);
            UserRepository
                .Setup(repository => repository.GetByIdAsync(DriverId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new User
                {
                    Id = DriverId,
                    FullName = "Test Driver",
                    Email = "driver@example.com",
                    Role = UserRole.Driver,
                    IsActive = true
                });
            Storage
                .Setup(service => service.UploadDriverSelfieAsync(
                    DriverId,
                    It.IsAny<byte[]>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync($"drivers/{DriverId}/profile.jpg");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JWT_SECRET"] = new string('l', 64),
                    ["Auth:DriverTokenExpiryMinutes"] = "15",
                    ["Auth:RefreshTokenExpiryDays"] = "30",
                    ["Liveness:NonceTtlMinutes"] = "2"
                })
                .Build();
            Service = new LivenessService(
                UserRepository.Object,
                Challenges.Object,
                Storage.Object,
                Notifications.Object,
                new UserSessionService(UserRepository.Object, new JwtHelper(configuration), configuration),
                configuration,
                HubContext.Object);
        }

        public Guid DriverId { get; } = Guid.NewGuid();
        public DriverProfile Profile { get; }
        public Mock<IUserRepository> UserRepository { get; } = new();
        public Mock<ILivenessChallengeRepository> Challenges { get; } = new();
        public Mock<IStorageService> Storage { get; } = new();
        public Mock<INotificationService> Notifications { get; } = new();
        public Mock<IHubContext<Hub>> HubContext { get; } = new();
        public LivenessService Service { get; }

        public LivenessChallenge AddValidChallenge()
        {
            var challenge = new LivenessChallenge
            {
                DriverId = DriverId,
                Nonce = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
                PromptsJson = JsonSerializer.Serialize(Prompts),
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(2)
            };
            Challenges
                .Setup(repository => repository.GetChallengeByNonceAsync(challenge.Nonce, It.IsAny<CancellationToken>()))
                .ReturnsAsync(challenge);
            return challenge;
        }
    }
}
