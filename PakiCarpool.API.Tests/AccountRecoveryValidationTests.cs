using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Auth;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Helpers;
using PakiCarpool.Services.Implementations;
using Xunit;

namespace PakiCarpool.API.Tests;

/// <summary>
/// Service-level coverage for backlog item 15 (account-recovery validation):
/// server-side password composition on register and password reset, per-account
/// email-OTP attempt locking, enumeration-safe OTP resend, and registration
/// rollback when the verification email cannot be delivered.
/// </summary>
public sealed class AccountRecoveryValidationTests
{
    private static readonly DateTime BaseTime = new(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);

    // ──────────────────────────────────────────────────────────────────────────
    // Password composition — register
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("abcdefg", "be at least 8 characters|contain an uppercase letter|contain a number|contain a symbol")]
    [InlineData("abcdefg!", "contain an uppercase letter|contain a number")]
    [InlineData("ABCDEFG1!", "contain a lowercase letter")]
    [InlineData("Upperlower!", "contain a number")]
    [InlineData("Upperlower1", "contain a symbol")]
    public async Task Register_WeakPassword_IsRejectedListingEveryMissingRequirementAtOnce(
        string password,
        string expectedMissingPipeSeparated)
    {
        var fixture = new AccountRecoveryFixture();

        var response = await fixture.AuthService.RegisterAsync(fixture.RegisterRequest(password));

        Assert.False(response.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationError, response.ErrorCode);
        foreach (var missing in expectedMissingPipeSeparated.Split('|'))
            Assert.Contains(missing, response.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(password, response.Message, StringComparison.Ordinal);
        fixture.Users.Verify(
            repository => repository.CreateWithDriverProfileAsync(
                It.IsAny<User>(),
                It.IsAny<DriverProfile>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Register_SevenCharacterPassword_IsRejectedAsTooShort()
    {
        var fixture = new AccountRecoveryFixture();
        var password = "Aa1!aaa"; // exactly 7 characters, otherwise compliant

        var response = await fixture.AuthService.RegisterAsync(fixture.RegisterRequest(password));

        Assert.False(response.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationError, response.ErrorCode);
        Assert.Contains("be at least 8 characters", response.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("uppercase", response.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_OneHundredTwentyNineCharacterPassword_IsRejectedAsTooLong()
    {
        var fixture = new AccountRecoveryFixture();
        // 126 compliant characters + 3 extra = 129
        var password = new string('a', 123) + "A1!" + "xyz";

        var response = await fixture.AuthService.RegisterAsync(fixture.RegisterRequest(password));

        Assert.False(response.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationError, response.ErrorCode);
        Assert.Contains("be at most 128 characters", response.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_ExactlyEightCharacterCompliantPassword_IsAccepted()
    {
        var fixture = new AccountRecoveryFixture();

        var response = await fixture.AuthService.RegisterAsync(fixture.RegisterRequest("Aa1!aaaa"));

        Assert.True(response.IsSuccess, response.Message);
        Assert.NotNull(fixture.CreatedUser);
        Assert.Equal(0, fixture.CreatedUser.EmailOtpResendCount);
        Assert.Null(fixture.CreatedUser.EmailOtpResendWindowStart);
        // The initial registration code records its send time for the resend cooldown…
        Assert.Equal(BaseTime, fixture.CreatedUser.EmailOtpLastSentAt);
        // …but never counts as a resend.
        Assert.Single(fixture.SentOtps);
    }

    [Fact]
    public async Task Register_ExactlyOneHundredTwentyEightCharacterCompliantPassword_IsAccepted()
    {
        var fixture = new AccountRecoveryFixture();
        var password = new string('a', 124) + "A1!";

        var response = await fixture.AuthService.RegisterAsync(fixture.RegisterRequest(password));

        Assert.True(response.IsSuccess, response.Message);
        Assert.Single(fixture.SentOtps);
    }

    [Fact]
    public async Task Register_PasswordWithSpacesAndUnicodeLetters_IsAccepted()
    {
        var fixture = new AccountRecoveryFixture();
        // A Unicode letter counts as a symbol under the shared Flutter/server
        // definition ([^A-Za-z0-9]); spaces and Unicode letters are allowed.
        var withUnicodeSymbol = "Passw0rdÜ";
        var withSpace = "Passw0rd 1!";

        var unicodeResponse = await fixture.AuthService.RegisterAsync(fixture.RegisterRequest(withUnicodeSymbol));
        var spaceResponse = await fixture.AuthService.RegisterAsync(fixture.RegisterRequest(withSpace));

        Assert.True(unicodeResponse.IsSuccess, unicodeResponse.Message);
        Assert.True(spaceResponse.IsSuccess, spaceResponse.Message);
        Assert.Equal(2, fixture.SentOtps.Count);
    }

    [Fact]
    public async Task Register_PasswordIsNeverTrimmedBeforeHashing()
    {
        var fixture = new AccountRecoveryFixture();
        // A trailing space is a symbol under the shared policy, so this password
        // is compliant — and must be hashed exactly as submitted (trailing space kept).
        var password = "Aa1!aaa ";

        var response = await fixture.AuthService.RegisterAsync(fixture.RegisterRequest(password));

        Assert.True(response.IsSuccess, response.Message);
        Assert.NotNull(fixture.CreatedUser);
        Assert.True(BCryptHelper.VerifyPassword(password, fixture.CreatedUser.PasswordHash!));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Password composition — password reset
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PasswordReset_WeakNewPassword_IsRejectedBeforeTheCodeIsChecked()
    {
        var fixture = new AccountRecoveryFixture();
        fixture.SeedResetCode();

        var response = await fixture.AuthService.ResetPasswordAsync(new ResetPasswordDto
        {
            Email = fixture.StoredUser.Email,
            Code = AccountRecoveryFixture.ResetCode,
            NewPassword = "weakpassword"
        });

        Assert.False(response.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationError, response.ErrorCode);
        Assert.Contains("contain an uppercase letter", response.Message, StringComparison.Ordinal);
        Assert.Contains("contain a number", response.Message, StringComparison.Ordinal);
        Assert.Contains("contain a symbol", response.Message, StringComparison.Ordinal);
        fixture.Users.Verify(
            repository => repository.CompletePasswordResetAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PasswordReset_CompliantNewPassword_IsAccepted()
    {
        var fixture = new AccountRecoveryFixture();
        fixture.SeedResetCode();
        string? persistedHash = null;
        fixture.Users.Setup(repository => repository.CompletePasswordResetAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string, DateTime, string, int, CancellationToken>((_, _, _, hash, _, _) => persistedHash = hash)
            .ReturnsAsync(true);

        var response = await fixture.AuthService.ResetPasswordAsync(new ResetPasswordDto
        {
            Email = fixture.StoredUser.Email,
            Code = AccountRecoveryFixture.ResetCode,
            NewPassword = "NewPassw0rd!"
        });

        Assert.True(response.IsSuccess, response.Message);
        Assert.NotNull(persistedHash);
        Assert.True(BCryptHelper.VerifyPassword("NewPassw0rd!", persistedHash!));
    }

    [Fact]
    public async Task AdminPasswordReset_WeakNewPassword_IsRejected()
    {
        var fixture = new AccountRecoveryFixture();
        fixture.StoredUser.Role = UserRole.Admin;

        var response = await fixture.AuthService.ResetAdminPasswordAsync(new AdminResetPasswordDto
        {
            Email = fixture.StoredUser.Email,
            Code = "123456",
            TotpCode = "123456",
            NewPassword = "stilltooweak"
        });

        Assert.False(response.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationError, response.ErrorCode);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Email OTP attempt limiting (per account)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_WrongOtpThreeTimes_LocksAccountForFifteenMinutesAndInvalidatesCode()
    {
        var fixture = new AccountRecoveryFixture();
        fixture.SeedEmailOtp();

        ApiResponse<object>? third = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await fixture.AuthService.VerifyEmailAsync(fixture.VerifyRequest("000000"));
            if (attempt < 2)
            {
                Assert.False(response.IsSuccess);
                Assert.Equal("Invalid verification code.", response.Message);
            }
            else
            {
                third = response;
            }
        }

        Assert.NotNull(third);
        Assert.False(third.IsSuccess);
        Assert.Equal("Too many invalid verification attempts. Try again later.", third.Message);
        Assert.Equal(3, fixture.StoredUser.EmailOtpAttempts);
        Assert.Equal(BaseTime.AddMinutes(15), fixture.StoredUser.EmailOtpLockedUntil);
        Assert.Null(fixture.StoredUser.EmailOtp);
        Assert.Null(fixture.StoredUser.EmailOtpExpiry);
    }

    [Fact]
    public async Task Verify_CorrectOtpDuringLock_IsRejectedWithTheSameGenericMessageAsAWrongCode()
    {
        var fixture = new AccountRecoveryFixture();
        fixture.SeedEmailOtp();
        fixture.StoredUser.EmailOtpAttempts = 3;
        fixture.StoredUser.EmailOtpLockedUntil = BaseTime.AddMinutes(10);

        var correct = await fixture.AuthService.VerifyEmailAsync(fixture.VerifyRequest(AccountRecoveryFixture.Otp));
        var wrong = await fixture.AuthService.VerifyEmailAsync(fixture.VerifyRequest("000000"));

        Assert.False(correct.IsSuccess);
        Assert.False(wrong.IsSuccess);
        Assert.Equal(wrong.Message, correct.Message);
        Assert.Equal("Too many invalid verification attempts. Try again later.", correct.Message);
        Assert.False(fixture.StoredUser.IsEmailVerified);
        // No remaining lock time is revealed.
        Assert.DoesNotContain("10", correct.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_CorrectOtpAfterLockExpires_SucceedsAndResetsTheAttemptCounter()
    {
        var fixture = new AccountRecoveryFixture();
        fixture.SeedEmailOtp();
        // A past lock is only reachable together with an issued code after expiry;
        // the service must reset the counter on success either way.
        fixture.StoredUser.EmailOtpAttempts = 3;
        fixture.StoredUser.EmailOtpLockedUntil = BaseTime.AddMinutes(-1);

        var response = await fixture.AuthService.VerifyEmailAsync(fixture.VerifyRequest(AccountRecoveryFixture.Otp));

        Assert.True(response.IsSuccess, response.Message);
        Assert.True(fixture.StoredUser.IsEmailVerified);
        Assert.Equal(0, fixture.StoredUser.EmailOtpAttempts);
        Assert.Null(fixture.StoredUser.EmailOtpLockedUntil);
        Assert.Null(fixture.StoredUser.EmailOtp);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Email OTP resend
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resend_DuringLock_IsRejectedWithoutIssuingOrResettingTheAttemptCounter()
    {
        var fixture = new AccountRecoveryFixture();
        fixture.SeedEmailOtp();
        fixture.StoredUser.EmailOtpAttempts = 3;
        fixture.StoredUser.EmailOtpLockedUntil = BaseTime.AddMinutes(10);

        var response = await fixture.AuthService.ResendEmailOtpAsync(fixture.ResendRequest());

        Assert.True(response.IsSuccess);
        Assert.Equal(AccountRecoveryFixture.ResendResponseMessage, response.Message);
        // The lock gate lives in the repository conditional update; it must be
        // consulted (and reject) rather than bypassed by the service.
        fixture.Users.Verify(
            repository => repository.TryIssueEmailOtpAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Email.Verify(
            service => service.SendEmailOtpAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
        Assert.Equal(3, fixture.StoredUser.EmailOtpAttempts);
        Assert.Equal(BaseTime.AddMinutes(10), fixture.StoredUser.EmailOtpLockedUntil);
    }

    [Fact]
    public async Task Resend_AfterLockExpires_IssuesANewCodeAndResetsTheAttemptCounter()
    {
        var fixture = new AccountRecoveryFixture();
        fixture.SeedEmailOtp();
        fixture.StoredUser.EmailOtpAttempts = 3;
        fixture.StoredUser.EmailOtpLockedUntil = BaseTime.AddMinutes(-1);
        var previousHash = fixture.StoredUser.EmailOtp;

        var response = await fixture.AuthService.ResendEmailOtpAsync(fixture.ResendRequest());

        Assert.True(response.IsSuccess);
        Assert.Equal(AccountRecoveryFixture.ResendResponseMessage, response.Message);
        Assert.Equal(0, fixture.StoredUser.EmailOtpAttempts);
        Assert.Null(fixture.StoredUser.EmailOtpLockedUntil);
        Assert.Single(fixture.SentOtps);
        // The replacement code differs from (and therefore invalidates) the previous one.
        Assert.NotEqual(previousHash, fixture.StoredUser.EmailOtp);
    }

    [Fact]
    public async Task Resend_UnknownEmail_ReturnsExactlyTheSameResponseAsAKnownEmail()
    {
        var fixture = new AccountRecoveryFixture();
        fixture.SeedEmailOtp();
        fixture.StoredUser.EmailOtpLastSentAt = BaseTime.AddMinutes(-5);

        var known = await fixture.AuthService.ResendEmailOtpAsync(fixture.ResendRequest());
        var unknown = await fixture.AuthService.ResendEmailOtpAsync(
            new ResendEmailVerificationDto { Email = "unknown@example.com" });
        // The first known resend succeeded, so the account is now cooling down —
        // the next known requests exercise the limit-rejected path too.
        var cooldownRejected = await fixture.AuthService.ResendEmailOtpAsync(fixture.ResendRequest());
        var cooldownRejectedAgain = await fixture.AuthService.ResendEmailOtpAsync(fixture.ResendRequest());

        Assert.Equal(known.IsSuccess, unknown.IsSuccess);
        Assert.Equal(known.Message, unknown.Message);
        Assert.Equal(known.Message, cooldownRejected.Message);
        Assert.Equal(known.Message, cooldownRejectedAgain.Message);
        Assert.Equal(AccountRecoveryFixture.ResendResponseMessage, unknown.Message);
    }

    [Fact]
    public async Task Resend_RejectedByRepositoryLimit_ReturnsTheSameGenericResponse()
    {
        var fixture = new AccountRecoveryFixture();
        fixture.SeedEmailOtp();
        fixture.StoredUser.EmailOtpLastSentAt = BaseTime; // cooldown still active
        var attemptsBefore = fixture.StoredUser.EmailOtpAttempts;

        var response = await fixture.AuthService.ResendEmailOtpAsync(fixture.ResendRequest());

        Assert.True(response.IsSuccess);
        Assert.Equal(AccountRecoveryFixture.ResendResponseMessage, response.Message);
        // The cooldown rejection came from the repository conditional update;
        // the attempt counter must be untouched by it.
        Assert.Equal(attemptsBefore, fixture.StoredUser.EmailOtpAttempts);
        fixture.Email.Verify(
            service => service.SendEmailOtpAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Registration recoverable when SMTP fails
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_WhenSmtpFails_RollsBackTheUnverifiedAccountSoReRegistrationWorks()
    {
        var fixture = new AccountRecoveryFixture();
        fixture.EmailThrows = true;
        var deletedUserIds = new List<Guid>();
        fixture.Users.Setup(repository => repository.DeleteUnverifiedUserAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((userId, _) => deletedUserIds.Add(userId))
            .ReturnsAsync(true);

        var failed = await fixture.AuthService.RegisterAsync(fixture.RegisterRequest("Aa1!aaaa"));

        Assert.False(failed.IsSuccess);
        Assert.Equal(ErrorCodes.InternalError, failed.ErrorCode);
        Assert.NotNull(fixture.CreatedUser);
        Assert.Equal([fixture.CreatedUser.Id], deletedUserIds);

        // The account was rolled back, so registering again with the same email works.
        fixture.EmailThrows = false;
        var retried = await fixture.AuthService.RegisterAsync(fixture.RegisterRequest("Aa1!aaaa"));

        Assert.True(retried.IsSuccess, retried.Message);
        Assert.Single(fixture.SentOtps);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Fixture
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class AccountRecoveryFixture
    {
        public const string Otp = "123456";
        public const string ResetCode = "654321";
        public const string ResendResponseMessage =
            "If an account exists for this email, a new verification code has been sent.";
        private const string JwtSecret = "account-recovery-test-secret-0123456789012345678901234567890123";

        public AccountRecoveryFixture()
        {
            Configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JWT_SECRET"] = JwtSecret,
                    ["JWT_ISSUER"] = "account-recovery-tests",
                    ["JWT_AUDIENCE"] = "account-recovery-test-clients",
                    ["AES_KEY_V1"] = Convert.ToBase64String(new byte[32]),
                    ["Auth:EmailOtpExpiryMinutes"] = "10",
                    ["Auth:EmailOtpAttemptLimit"] = "3",
                    ["Auth:EmailOtpLockoutMinutes"] = "15",
                    ["Auth:EmailOtpResendCooldownSeconds"] = "60",
                    ["Auth:EmailOtpResendLimit"] = "3",
                    ["Auth:EmailOtpResendWindowMinutes"] = "15",
                    ["Auth:PasswordResetCodeExpiryMinutes"] = "10"
                })
                .Build();
            Time = new MutableTimeProvider(BaseTime);
            StoredUser = new User
            {
                Id = Guid.NewGuid(),
                FullName = "Account Recovery User",
                Email = "recover@example.com",
                Role = UserRole.Passenger,
                IsActive = true,
                IsEmailVerified = false,
                TrustScore = 100
            };

            Users.Setup(repository => repository.GetByEmailAsync(
                    It.Is<string>(email => email == StoredUser.Email),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => CloneUser(StoredUser));
            Users.Setup(repository => repository.EmailExistsAsync(It.IsAny<string>()))
                .ReturnsAsync(false);
            Users.Setup(repository => repository.CreateWithDriverProfileAsync(
                    It.IsAny<User>(),
                    It.IsAny<DriverProfile>(),
                    It.IsAny<CancellationToken>()))
                .Callback<User, DriverProfile?, CancellationToken>((user, _, _) => CreatedUser = user)
                .ReturnsAsync((User user, DriverProfile? _, CancellationToken _) => user);
            Users.Setup(repository => repository.UpdateAsync(
                    It.IsAny<User>(),
                    It.IsAny<CancellationToken>()))
                .Callback<User, CancellationToken>((user, _) => CopyInto(user, StoredUser))
                .Returns(Task.CompletedTask);
            Users.Setup(repository => repository.ResetLoginFailAsync(
                    It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Users.Setup(repository => repository.DeleteUnverifiedUserAsync(
                    It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // In-memory simulation of the atomic per-account OTP attempt counter,
            // mirroring UserRepository.IncrementEmailOtpAttemptsAsync exactly.
            Users.Setup(repository => repository.IncrementEmailOtpAttemptsAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<int>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Guid userId, DateTime now, int attemptLimit, DateTime lockoutUntil, CancellationToken _) =>
                {
                    IncrementAttempts(userId, now, attemptLimit, lockoutUntil);
                    return Task.FromResult(true);
                });

            // In-memory simulation of the atomic resend gate, mirroring
            // UserRepository.TryIssueEmailOtpAsync exactly.
            Users.Setup(repository => repository.TryIssueEmailOtpAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Guid userId, string codeHash, DateTime now, DateTime expiresAt,
                    TimeSpan cooldown, TimeSpan window, int limit, CancellationToken _) =>
                {
                    var issued = TryIssue(userId, codeHash, now, expiresAt, cooldown, window, limit);
                    return Task.FromResult(issued);
                });

            Users.Setup(repository => repository.CompletePasswordResetAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            Email.Setup(service => service.SendEmailOtpAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Callback<string, string, string>((_, _, otp) =>
                {
                    if (EmailThrows)
                        throw new InvalidOperationException("Simulated SMTP failure.");
                    SentOtps.Add(otp);
                })
                .Returns(Task.CompletedTask);

            AuthService = new AuthService(
                Users.Object,
                Sessions.Object,
                Rides.Object,
                Email.Object,
                new JwtHelper(Configuration),
                new AesEncryptionHelper(Configuration),
                OAuthStates.Object,
                HttpClientFactory.Object,
                Configuration,
                Captcha.Object,
                NullLogger<AuthService>.Instance,
                Time);
        }

        public IConfiguration Configuration { get; }
        public MutableTimeProvider Time { get; }
        public User StoredUser { get; }
        public User? CreatedUser { get; private set; }
        public bool EmailThrows { get; set; }
        public List<string> SentOtps { get; } = [];
        public AuthService AuthService { get; }
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IUserSessionService> Sessions { get; } = new();
        public Mock<IRideRepository> Rides { get; } = new();
        public Mock<IEmailService> Email { get; } = new();
        public Mock<IOAuthStateStore> OAuthStates { get; } = new();
        public Mock<IHttpClientFactory> HttpClientFactory { get; } = new();
        public Mock<IHCaptchaService> Captcha { get; } = new();

        public RegisterRequestDto RegisterRequest(string password) => new()
        {
            FullName = "Account Recovery User",
            Email = StoredUser.Email,
            Password = password,
            Role = UserRole.Passenger
        };

        public VerifyEmailDto VerifyRequest(string otp) => new()
        {
            Email = StoredUser.Email,
            Otp = otp
        };

        public ResendEmailVerificationDto ResendRequest() => new() { Email = StoredUser.Email };

        public void SeedEmailOtp()
        {
            StoredUser.IsEmailVerified = false;
            StoredUser.EmailOtp = HashOtp(Otp);
            StoredUser.EmailOtpExpiry = BaseTime.AddMinutes(10);
            StoredUser.EmailOtpAttempts = 0;
            StoredUser.EmailOtpLockedUntil = null;
            StoredUser.EmailOtpLastSentAt = BaseTime.AddMinutes(-5);
            StoredUser.EmailOtpResendCount = 0;
            StoredUser.EmailOtpResendWindowStart = null;
        }

        public void SeedResetCode()
        {
            StoredUser.Role = UserRole.Passenger;
            StoredUser.PasswordResetToken = HashResetCode(ResetCode);
            // The reset flow compares against DateTime.UtcNow, so seed a real-future expiry.
            StoredUser.PasswordResetTokenExpiry = DateTime.UtcNow.AddMinutes(10);
            StoredUser.PasswordResetAttempts = 0;
        }

        private void IncrementAttempts(Guid userId, DateTime now, int attemptLimit, DateTime lockoutUntil)
        {
            if (StoredUser.Id != userId || StoredUser.EmailOtp is null ||
                StoredUser.EmailOtpExpiry <= now ||
                StoredUser.EmailOtpLockedUntil is not null ||
                StoredUser.EmailOtpAttempts >= attemptLimit)
                return;

            StoredUser.EmailOtpAttempts++;
            if (StoredUser.EmailOtpAttempts >= attemptLimit)
            {
                StoredUser.EmailOtpLockedUntil = lockoutUntil;
                StoredUser.EmailOtp = null;
                StoredUser.EmailOtpExpiry = null;
            }
        }

        private bool TryIssue(
            Guid userId,
            string codeHash,
            DateTime now,
            DateTime expiresAt,
            TimeSpan cooldown,
            TimeSpan window,
            int limit)
        {
            if (StoredUser.Id != userId || StoredUser.Role == UserRole.Admin ||
                !StoredUser.IsActive || StoredUser.IsEmailVerified)
                return false;
            if (StoredUser.EmailOtpLockedUntil is not null && StoredUser.EmailOtpLockedUntil > now)
                return false;
            if (StoredUser.EmailOtpLastSentAt is not null && StoredUser.EmailOtpLastSentAt > now - cooldown)
                return false;
            if (StoredUser.EmailOtpResendWindowStart is not null &&
                StoredUser.EmailOtpResendWindowStart > now - window &&
                StoredUser.EmailOtpResendCount >= limit)
                return false;

            if (StoredUser.EmailOtpLockedUntil is not null)
                StoredUser.EmailOtpAttempts = 0;
            StoredUser.EmailOtpLockedUntil = null;
            StoredUser.EmailOtp = codeHash;
            StoredUser.EmailOtpExpiry = expiresAt;
            StoredUser.EmailOtpLastSentAt = now;
            if (StoredUser.EmailOtpResendWindowStart is null ||
                StoredUser.EmailOtpResendWindowStart <= now - window)
            {
                StoredUser.EmailOtpResendCount = 1;
                StoredUser.EmailOtpResendWindowStart = now;
            }
            else
            {
                StoredUser.EmailOtpResendCount++;
            }

            return true;
        }

        private static string HashOtp(string otp)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(otp))).ToLowerInvariant();

        private static string HashResetCode(string code)
            => Convert.ToHexString(
                    HMACSHA256.HashData(
                        Encoding.UTF8.GetBytes(JwtSecret),
                        Encoding.UTF8.GetBytes(code)))
                .ToLowerInvariant();

        private static User CloneUser(User user)
        {
            var clone = new User();
            CopyInto(user, clone);
            return clone;
        }

        private static void CopyInto(User source, User target)
        {
            target.Id = source.Id;
            target.FullName = source.FullName;
            target.Email = source.Email;
            target.PasswordHash = source.PasswordHash;
            target.Role = source.Role;
            target.GoogleId = source.GoogleId;
            target.IsEmailVerified = source.IsEmailVerified;
            target.EmailOtp = source.EmailOtp;
            target.EmailOtpExpiry = source.EmailOtpExpiry;
            target.EmailOtpAttempts = source.EmailOtpAttempts;
            target.EmailOtpLockedUntil = source.EmailOtpLockedUntil;
            target.EmailOtpLastSentAt = source.EmailOtpLastSentAt;
            target.EmailOtpResendCount = source.EmailOtpResendCount;
            target.EmailOtpResendWindowStart = source.EmailOtpResendWindowStart;
            target.PasswordResetToken = source.PasswordResetToken;
            target.PasswordResetTokenExpiry = source.PasswordResetTokenExpiry;
            target.PasswordResetAttempts = source.PasswordResetAttempts;
            target.PasswordResetRequestCount = source.PasswordResetRequestCount;
            target.PasswordResetRequestWindowStart = source.PasswordResetRequestWindowStart;
            target.AdminPasswordResetCodeHash = source.AdminPasswordResetCodeHash;
            target.AdminPasswordResetCodeExpiry = source.AdminPasswordResetCodeExpiry;
            target.AdminPasswordResetAttempts = source.AdminPasswordResetAttempts;
            target.AdminPasswordResetRequestCount = source.AdminPasswordResetRequestCount;
            target.AdminPasswordResetRequestWindowStart = source.AdminPasswordResetRequestWindowStart;
            target.AuthTokenVersion = source.AuthTokenVersion;
            target.IsActive = source.IsActive;
            target.IsSuspended = source.IsSuspended;
            target.TrustScore = source.TrustScore;
            target.FailedLoginCount = source.FailedLoginCount;
            target.LockoutUntil = source.LockoutUntil;
            target.LoginFailCount = source.LoginFailCount;
            target.LoginBannedUntil = source.LoginBannedUntil;
            target.CreatedAt = source.CreatedAt;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public void AdvanceTo(DateTimeOffset value) => _utcNow = value;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
