using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Auth;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Repository.EFCore;
using PakiCarpool.Repository.EFCore.Repositories;
using PakiCarpool.Security.Helpers;
using PakiCarpool.Services.Implementations;
using Xunit;

namespace PakiCarpool.API.Tests;

public sealed class RefreshPostgreSqlTests
{
    private const string ConnectionStringEnvironmentVariable = "PAKICARPOOL_TEST_CONNECTIONSTRING";
    private const string RawRefreshToken = "postgresql-concurrent-refresh-token";

    [PostgreSqlFact]
    public async Task ConcurrentDoubleRefresh_ProducesOnlyOneSession()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        var firstService = fixture.CreateAuthService(firstContext);
        var secondService = fixture.CreateAuthService(secondContext);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstAttempt = RefreshAfterStartAsync(firstService, start.Task);
        var secondAttempt = RefreshAfterStartAsync(secondService, start.Task);
        start.SetResult();
        var responses = await Task.WhenAll(firstAttempt, secondAttempt);

        var successful = Assert.Single(responses, response => response.IsSuccess);
        var rejected = Assert.Single(responses, response => !response.IsSuccess);
        Assert.Equal(ErrorCodes.TokenExpired, rejected.Message);

        await using var verificationContext = fixture.CreateContext();
        var tokens = await verificationContext.RefreshTokens
            .AsNoTracking()
            .Where(token => token.UserId == fixture.User.Id)
            .ToListAsync();
        Assert.Equal(2, tokens.Count);
        Assert.True(Assert.Single(tokens, token => token.Id == fixture.StoredToken.Id).IsRevoked);
        var replacement = Assert.Single(tokens, token => token.Id != fixture.StoredToken.Id);
        Assert.False(replacement.IsRevoked);
        Assert.Equal(successful.Data!.SessionIssuedAt, replacement.SessionIssuedAt);
    }

    private static async Task<ApiResponse<AuthResponseDto>> RefreshAfterStartAsync(
        AuthService authService,
        Task start)
    {
        await start;
        return await authService.RefreshTokenAsync(RawRefreshToken);
    }

    private sealed class PostgreSqlFixture : IAsyncDisposable
    {
        private readonly string _baseConnectionString;
        private readonly DbContextOptions<PakiCarpoolDbContext> _options;
        private readonly IConfiguration _configuration;
        private readonly JwtHelper _jwtHelper;
        private PakiCarpoolDbContext? _seedContext;

        private PostgreSqlFixture(string baseConnectionString)
        {
            _baseConnectionString = baseConnectionString;
            Schema = $"refresh_rotation_{Guid.NewGuid():N}";
            var builder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                SearchPath = Schema
            };
            _options = new DbContextOptionsBuilder<PakiCarpoolDbContext>()
                .UseNpgsql(builder.ConnectionString)
                .Options;
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JWT_SECRET"] = new string('r', 64),
                    ["JWT_ISSUER"] = "refresh-postgresql-tests",
                    ["JWT_AUDIENCE"] = "refresh-postgresql-test-clients",
                    ["AES_KEY_V1"] = Convert.ToBase64String(new byte[32]),
                    ["Auth:PassengerTokenExpiryMinutes"] = "30",
                    ["Auth:DriverTokenExpiryMinutes"] = "15",
                    ["Auth:RefreshTokenExpiryDays"] = "7"
                })
                .Build();
            _jwtHelper = new JwtHelper(_configuration);
        }

        private string Schema { get; }
        public User User { get; private set; } = null!;
        public RefreshToken StoredToken { get; private set; } = null!;

        public static async Task<PostgreSqlFixture> CreateAsync()
        {
            var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
                ?? throw new InvalidOperationException("PostgreSQL test connection string is missing.");
            var fixture = new PostgreSqlFixture(connectionString);
            try
            {
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

        public AuthService CreateAuthService(PakiCarpoolDbContext context)
        {
            var userRepository = new UserRepository(context);
            var sessionService = new UserSessionService(userRepository, _jwtHelper, _configuration);
            return new AuthService(
                userRepository,
                sessionService,
                new Mock<IRideRepository>().Object,
                new Mock<IEmailService>().Object,
                _jwtHelper,
                new AesEncryptionHelper(_configuration),
                new Mock<IOAuthStateStore>().Object,
                new Mock<IHttpClientFactory>().Object,
                _configuration,
                new Mock<IHCaptchaService>().Object,
                NullLogger<AuthService>.Instance);
        }

        private async Task InitializeAsync()
        {
            await using (var connection = new NpgsqlConnection(_baseConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE SCHEMA \"{Schema}\"";
                await command.ExecuteNonQueryAsync();
            }

            _seedContext = CreateContext();
            await _seedContext.Database.EnsureCreatedAsync();
            var issuedAt = DateTime.UtcNow.AddHours(-1);
            User = new User
            {
                Id = Guid.NewGuid(),
                FullName = "PostgreSQL Refresh Test User",
                Email = $"{Guid.NewGuid():N}@example.com",
                Role = UserRole.Passenger,
                IsEmailVerified = true,
                IsActive = true,
                IsSuspended = false
            };
            StoredToken = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = User.Id,
                Token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RawRefreshToken)))
                    .ToLowerInvariant(),
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = issuedAt,
                SessionIssuedAt = issuedAt
            };
            _seedContext.AddRange(User, StoredToken);
            await _seedContext.SaveChangesAsync();
            _seedContext.ChangeTracker.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            if (_seedContext is not null)
                await _seedContext.DisposeAsync();

            await using var connection = new NpgsqlConnection(_baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP SCHEMA IF EXISTS \"{Schema}\" CASCADE";
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
