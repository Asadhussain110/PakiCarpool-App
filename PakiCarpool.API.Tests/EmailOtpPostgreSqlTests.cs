using Microsoft.EntityFrameworkCore;
using Npgsql;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Repository.EFCore;
using PakiCarpool.Repository.EFCore.Repositories;
using Xunit;

namespace PakiCarpool.API.Tests;

/// <summary>
/// PostgreSQL integration coverage for backlog item 15: the per-account email-OTP
/// attempt counter, lockout, resend cooldown, resend window, code invalidation, and
/// the unverified-user rollback delete are enforced atomically per account (not per IP)
/// by conditional database updates in UserRepository.
/// </summary>
public sealed class EmailOtpPostgreSqlTests
{
    public const string ConnectionStringEnvironmentVariable = "PAKICARPOOL_TEST_CONNECTIONSTRING";

    private static readonly DateTime T0 = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private const int ResendLimit = 3;
    private const int AttemptLimit = 3;

    [PostgreSqlFact]
    public async Task Resend_WithinSixtySecondsOfTheLastSend_IsRejected()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var repository = new UserRepository(context);

        Assert.True(await repository.TryIssueEmailOtpAsync(
            fixture.UserId, "hash-1", T0, T0.AddMinutes(10), Cooldown, Window, ResendLimit));
        Assert.False(await repository.TryIssueEmailOtpAsync(
            fixture.UserId, "hash-2", T0.AddSeconds(30), T0.AddSeconds(30).AddMinutes(10), Cooldown, Window, ResendLimit));

        var user = await fixture.ReloadUserAsync();
        Assert.Equal("hash-1", user.EmailOtp);
        Assert.Equal(T0, user.EmailOtpLastSentAt);
    }

    [PostgreSqlFact]
    public async Task Resend_AfterTheCooldown_SucceedsAndInvalidatesThePreviousCode()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var repository = new UserRepository(context);

        Assert.True(await repository.TryIssueEmailOtpAsync(
            fixture.UserId, "hash-1", T0, T0.AddMinutes(10), Cooldown, Window, ResendLimit));
        Assert.True(await repository.TryIssueEmailOtpAsync(
            fixture.UserId, "hash-2", T0.AddSeconds(61), T0.AddSeconds(61).AddMinutes(10), Cooldown, Window, ResendLimit));

        var user = await fixture.ReloadUserAsync();
        Assert.Equal("hash-2", user.EmailOtp);
        Assert.Equal(T0.AddSeconds(61), user.EmailOtpLastSentAt);
    }

    [PostgreSqlFact]
    public async Task FourthResend_WithinTheFifteenMinuteWindow_IsRejected()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var repository = new UserRepository(context);

        Assert.True(await repository.TryIssueEmailOtpAsync(
            fixture.UserId, "hash-1", T0, T0.AddMinutes(10), Cooldown, Window, ResendLimit));
        Assert.True(await repository.TryIssueEmailOtpAsync(
            fixture.UserId, "hash-2", T0.AddSeconds(61), T0.AddMinutes(10), Cooldown, Window, ResendLimit));
        Assert.True(await repository.TryIssueEmailOtpAsync(
            fixture.UserId, "hash-3", T0.AddSeconds(122), T0.AddMinutes(10), Cooldown, Window, ResendLimit));
        Assert.False(await repository.TryIssueEmailOtpAsync(
            fixture.UserId, "hash-4", T0.AddSeconds(183), T0.AddMinutes(10), Cooldown, Window, ResendLimit));

        var user = await fixture.ReloadUserAsync();
        Assert.Equal("hash-3", user.EmailOtp);
        Assert.Equal(3, user.EmailOtpResendCount);
        Assert.Equal(T0, user.EmailOtpResendWindowStart);
    }

    [PostgreSqlFact]
    public async Task ResendWindow_ResetsAfterFifteenMinutes()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var repository = new UserRepository(context);

        for (var resend = 0; resend < ResendLimit; resend++)
        {
            var now = T0.AddSeconds(61 * resend);
            Assert.True(await repository.TryIssueEmailOtpAsync(
                fixture.UserId, $"hash-{resend}", now, now.AddMinutes(10), Cooldown, Window, ResendLimit));
        }

        Assert.False(await repository.TryIssueEmailOtpAsync(
            fixture.UserId, "hash-blocked", T0.AddMinutes(14), T0.AddMinutes(24), Cooldown, Window, ResendLimit));

        var afterWindow = T0.AddMinutes(15).AddSeconds(1);
        Assert.True(await repository.TryIssueEmailOtpAsync(
            fixture.UserId, "hash-new-window", afterWindow, afterWindow.AddMinutes(10), Cooldown, Window, ResendLimit));

        var user = await fixture.ReloadUserAsync();
        Assert.Equal("hash-new-window", user.EmailOtp);
        Assert.Equal(1, user.EmailOtpResendCount);
        Assert.Equal(afterWindow, user.EmailOtpResendWindowStart);
    }

    [PostgreSqlFact]
    public async Task Resend_WhileTheAccountIsOtpLocked_IsRejectedWithoutTouchingTheCounter()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var repository = new UserRepository(context);
        await fixture.SetLockAsync(attempts: 3, lockedUntil: T0.AddMinutes(10));

        Assert.False(await repository.TryIssueEmailOtpAsync(
            fixture.UserId, "hash-locked", T0, T0.AddMinutes(10), Cooldown, Window, ResendLimit));

        var user = await fixture.ReloadUserAsync();
        Assert.Equal(3, user.EmailOtpAttempts);
        Assert.Equal(T0.AddMinutes(10), user.EmailOtpLockedUntil);
    }

    [PostgreSqlFact]
    public async Task Resend_AfterTheLockExpires_ResetsTheAttemptCounter()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var repository = new UserRepository(context);
        await fixture.SetLockAsync(attempts: 3, lockedUntil: T0.AddMinutes(-1));

        var afterLock = T0.AddMinutes(5);
        Assert.True(await repository.TryIssueEmailOtpAsync(
            fixture.UserId, "hash-after-lock", afterLock, afterLock.AddMinutes(10), Cooldown, Window, ResendLimit));

        var user = await fixture.ReloadUserAsync();
        Assert.Equal(0, user.EmailOtpAttempts);
        Assert.Null(user.EmailOtpLockedUntil);
        Assert.Equal("hash-after-lock", user.EmailOtp);
    }

    [PostgreSqlFact]
    public async Task Resend_WhenTheAccountWasNeverLocked_DoesNotResetTheAttemptCounter()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var repository = new UserRepository(context);
        await fixture.SetAttemptsAsync(2);

        Assert.True(await repository.TryIssueEmailOtpAsync(
            fixture.UserId, "hash-resend", T0, T0.AddMinutes(10), Cooldown, Window, ResendLimit));

        var user = await fixture.ReloadUserAsync();
        Assert.Equal(2, user.EmailOtpAttempts);
    }

    [PostgreSqlFact]
    public async Task ThirdWrongAttempt_LocksTheAccountAndInvalidatesTheCode()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var repository = new UserRepository(context);
        var lockoutUntil = T0.AddMinutes(15);

        Assert.True(await repository.IncrementEmailOtpAttemptsAsync(fixture.UserId, T0, AttemptLimit, lockoutUntil));
        Assert.True(await repository.IncrementEmailOtpAttemptsAsync(fixture.UserId, T0, AttemptLimit, lockoutUntil));
        Assert.True(await repository.IncrementEmailOtpAttemptsAsync(fixture.UserId, T0, AttemptLimit, lockoutUntil));
        // Already locked and invalidated — further attempts are not counted.
        Assert.False(await repository.IncrementEmailOtpAttemptsAsync(fixture.UserId, T0, AttemptLimit, lockoutUntil));

        var user = await fixture.ReloadUserAsync();
        Assert.Equal(3, user.EmailOtpAttempts);
        Assert.Equal(lockoutUntil, user.EmailOtpLockedUntil);
        Assert.Null(user.EmailOtp);
        Assert.Null(user.EmailOtpExpiry);
    }

    [PostgreSqlFact]
    public async Task WrongAttemptCounting_IsPerAccount_NotPerIp()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var repository = new UserRepository(context);
        var lockoutUntil = T0.AddMinutes(15);

        // Three wrong attempts and a lock for the first account…
        for (var attempt = 0; attempt < AttemptLimit; attempt++)
            await repository.IncrementEmailOtpAttemptsAsync(fixture.UserId, T0, AttemptLimit, lockoutUntil);
        var lockedUser = await fixture.ReloadUserAsync();
        Assert.NotNull(lockedUser.EmailOtpLockedUntil);

        // …leave a second account untouched, even from the same origin.
        Assert.True(await repository.TryIssueEmailOtpAsync(
            fixture.OtherUserId, "hash-other", T0, T0.AddMinutes(10), Cooldown, Window, ResendLimit));
        var otherUser = await fixture.ReloadOtherUserAsync();
        Assert.Equal(0, otherUser.EmailOtpAttempts);
        Assert.Null(otherUser.EmailOtpLockedUntil);
        Assert.Equal("hash-other", otherUser.EmailOtp);
    }

    [PostgreSqlFact]
    public async Task Resend_IsRejectedForVerifiedAdminAndInactiveAccounts()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var repository = new UserRepository(context);

        Assert.False(await repository.TryIssueEmailOtpAsync(
            fixture.VerifiedUserId, "hash-verified", T0, T0.AddMinutes(10), Cooldown, Window, ResendLimit));
        Assert.False(await repository.TryIssueEmailOtpAsync(
            fixture.AdminUserId, "hash-admin", T0, T0.AddMinutes(10), Cooldown, Window, ResendLimit));
        Assert.False(await repository.TryIssueEmailOtpAsync(
            fixture.InactiveUserId, "hash-inactive", T0, T0.AddMinutes(10), Cooldown, Window, ResendLimit));
    }

    [PostgreSqlFact]
    public async Task DeleteUnverifiedUser_RemovesTheUserAndDriverProfile_AndKeepsVerifiedUsers()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var repository = new UserRepository(context);
        var driverUserId = Guid.NewGuid();
        await context.Users.AddAsync(new User
        {
            Id = driverUserId,
            FullName = "Rollback Driver",
            Email = $"rollback-{Guid.NewGuid():N}@example.com",
            Role = UserRole.Driver,
            IsActive = true,
            IsEmailVerified = false
        });
        await context.DriverProfiles.AddAsync(new DriverProfile
        {
            Id = Guid.NewGuid(),
            UserId = driverUserId,
            LivenessVerified = false
        });
        await context.SaveChangesAsync();

        Assert.True(await repository.DeleteUnverifiedUserAsync(driverUserId));
        Assert.False(await repository.DeleteUnverifiedUserAsync(driverUserId));
        Assert.Null(await context.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == driverUserId));
        Assert.Empty(await context.DriverProfiles.AsNoTracking()
            .Where(profile => profile.UserId == driverUserId).ToListAsync());

        // A verified account is never deleted by the rollback path.
        Assert.False(await repository.DeleteUnverifiedUserAsync(fixture.VerifiedUserId));
        Assert.NotNull(await context.Users.AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == fixture.VerifiedUserId));
    }

    private sealed class PostgreSqlFixture : IAsyncDisposable
    {
        private readonly string _adminConnectionString;
        private readonly string _connectionString;
        private readonly DbContextOptions<PakiCarpoolDbContext> _options;
        private PakiCarpoolDbContext? _seedContext;

        private PostgreSqlFixture(string baseConnectionString)
        {
            _adminConnectionString = baseConnectionString;
            DatabaseName = $"paki_email_otp_{Guid.NewGuid():N}";
            var builder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = DatabaseName
            };
            _connectionString = builder.ConnectionString;
            _options = new DbContextOptionsBuilder<PakiCarpoolDbContext>()
                .UseNpgsql(_connectionString)
                .Options;
        }

        public string DatabaseName { get; }
        public Guid UserId { get; private set; }
        public Guid OtherUserId { get; private set; }
        public Guid VerifiedUserId { get; private set; }
        public Guid AdminUserId { get; private set; }
        public Guid InactiveUserId { get; private set; }

        public static async Task<PostgreSqlFixture> CreateAsync()
        {
            var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
                ?? throw new InvalidOperationException("PostgreSQL test connection string is missing.");
            var fixture = new PostgreSqlFixture(connectionString);
            try
            {
                await fixture.CreateDatabaseAsync();
                await fixture.InitializeAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public PakiCarpoolDbContext CreateContext() => new(_options);

        public Task<User> ReloadUserAsync() => ReloadAsync(UserId);

        public Task<User> ReloadOtherUserAsync() => ReloadAsync(OtherUserId);

        public async Task SetLockAsync(int attempts, DateTime lockedUntil)
        {
            await using var context = CreateContext();
            await context.Users
                .Where(user => user.Id == UserId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(user => user.EmailOtpAttempts, attempts)
                    .SetProperty(user => user.EmailOtpLockedUntil, lockedUntil));
        }

        public async Task SetAttemptsAsync(int attempts)
        {
            await using var context = CreateContext();
            await context.Users
                .Where(user => user.Id == UserId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(user => user.EmailOtpAttempts, attempts)
                    .SetProperty(user => user.EmailOtpLockedUntil, (DateTime?)null));
        }

        private async Task<User> ReloadAsync(Guid userId)
        {
            await using var context = CreateContext();
            return await context.Users.AsNoTracking().SingleAsync(user => user.Id == userId);
        }

        private async Task CreateDatabaseAsync()
        {
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{DatabaseName}\"";
            await command.ExecuteNonQueryAsync();
        }

        private async Task InitializeAsync()
        {
            _seedContext = CreateContext();
            await _seedContext.Database.EnsureCreatedAsync();
            UserId = Guid.NewGuid();
            OtherUserId = Guid.NewGuid();
            VerifiedUserId = Guid.NewGuid();
            AdminUserId = Guid.NewGuid();
            InactiveUserId = Guid.NewGuid();
            _seedContext.Users.AddRange(
                BuildUser(UserId, "OTP Limitee", UserRole.Passenger, verified: false, active: true),
                BuildUser(OtherUserId, "Second Account", UserRole.Passenger, verified: false, active: true),
                BuildUser(VerifiedUserId, "Verified User", UserRole.Passenger, verified: true, active: true),
                BuildUser(AdminUserId, "Admin User", UserRole.Admin, verified: true, active: true),
                BuildUser(InactiveUserId, "Inactive User", UserRole.Passenger, verified: false, active: false));
            await _seedContext.SaveChangesAsync();
            _seedContext.ChangeTracker.Clear();

            // Give the primary test account a pending, unexpired code so the
            // attempt counter is reachable.
            await SetAttemptsAsync(0);
            await using var context = CreateContext();
            await context.Users
                .Where(user => user.Id == UserId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(user => user.EmailOtp, "hash-initial")
                    .SetProperty(user => user.EmailOtpExpiry, T0.AddHours(1)));
        }

        private static User BuildUser(Guid id, string fullName, UserRole role, bool verified, bool active) => new()
        {
            Id = id,
            FullName = fullName,
            Email = $"{id:N}@example.com",
            Role = role,
            IsActive = active,
            IsEmailVerified = verified,
            TrustScore = 100,
            CreatedAt = DateTime.UtcNow
        };

        public async ValueTask DisposeAsync()
        {
            if (_seedContext is not null)
                await _seedContext.DisposeAsync();

            NpgsqlConnection.ClearPool(new NpgsqlConnection(_connectionString));
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS \"{DatabaseName}\"";
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class PostgreSqlFactAttribute : FactAttribute
    {
        public PostgreSqlFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)))
            {
                Skip = $"Requires a real PostgreSQL database via {ConnectionStringEnvironmentVariable}.";
            }
        }
    }
}
