using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Npgsql;
using PakiCarpool.Domain.DTOs.Driver;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Repository.EFCore;
using PakiCarpool.Repository.EFCore.Repositories;
using PakiCarpool.Repository.EFCore.ValueConverters;
using PakiCarpool.Repository.Migrations;
using PakiCarpool.Security.Helpers;
using PakiCarpool.Services.Implementations;
using Xunit;

namespace PakiCarpool.API.Tests;

public sealed class LicensePlateEncryptionPostgreSqlTests
{
    private const string ConnectionStringEnvironmentVariable = "PAKICARPOOL_TEST_CONNECTIONSTRING";
    private static readonly string TestKey = Convert.ToBase64String(new byte[32]);
    private static readonly IReadOnlyDictionary<byte, string> TestKeys =
        new Dictionary<byte, string> { [1] = TestKey };

    [PostgreSqlFact]
    public async Task RegisterCar_StoresOneEncryptedLayer_AndReadsBackPlaintext()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var service = fixture.CreateDriverService(context);

        var response = await service.RegisterCarAsync(
            fixture.DriverUserId,
            new RegisterCarDto
            {
                Make = "Test",
                Model = "Car",
                Year = 2026,
                LicensePlate = " test-new-12 ",
                AvailableSeats = 3,
                PhotoPaths = [$"private://driver-cars/{fixture.DriverUserId:D}/{Guid.NewGuid():N}.jpg"],
                RegDocPath = $"private://driver-registration-documents/{fixture.DriverUserId:D}/{Guid.NewGuid():N}.png"
            });

        Assert.True(response.IsSuccess);
        var carId = response.Data!.Id;
        var storedCiphertext = await fixture.ReadRawLicensePlateAsync(carId);
        Assert.Equal("TEST-NEW-12", DecryptOneLayer(storedCiphertext));
        Assert.Equal("TEST-NEW-12", await fixture.ReadLicensePlateAsync(carId));
    }

    [PostgreSqlFact]
    public async Task DoubleEncryptedRow_ReadsBeforeAndAfterMigration()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        var carId = Guid.NewGuid();
        await fixture.InsertRawCarAsync(carId, fixture.EncryptTwice("TEST-OLD-12"));

        Assert.Equal("TEST-OLD-12", await fixture.ReadLicensePlateAsync(carId));

        await fixture.MigrateAsync();

        Assert.Equal("TEST-OLD-12", await fixture.ReadLicensePlateAsync(carId));
        var migratedCiphertext = await fixture.ReadRawLicensePlateAsync(carId);
        Assert.Equal("TEST-OLD-12", DecryptOneLayer(migratedCiphertext));
    }

    [PostgreSqlFact]
    public async Task Migration_RunTwice_LeavesCiphertextUnchanged()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        var carId = Guid.NewGuid();
        await fixture.InsertRawCarAsync(carId, fixture.EncryptTwice("TEST-IDEMPOTENT-12"));

        await fixture.MigrateAsync();
        var firstResult = await fixture.ReadRawLicensePlateAsync(carId);
        await fixture.MigrateAsync();
        var secondResult = await fixture.ReadRawLicensePlateAsync(carId);

        Assert.Equal(firstResult, secondResult, StringComparer.Ordinal);
        Assert.Equal("TEST-IDEMPOTENT-12", await fixture.ReadLicensePlateAsync(carId));
    }

    [PostgreSqlFact]
    public async Task Migration_SingleEncryptedRow_IsLeftUntouched()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        var carId = Guid.NewGuid();
        await fixture.InsertRawCarAsync(carId, fixture.EncryptOnce("TEST-SINGLE-12"));
        var originalCiphertext = await fixture.ReadRawLicensePlateAsync(carId);

        await fixture.MigrateAsync();

        var migratedCiphertext = await fixture.ReadRawLicensePlateAsync(carId);
        Assert.Equal(originalCiphertext, migratedCiphertext, StringComparer.Ordinal);
        Assert.Equal("TEST-SINGLE-12", await fixture.ReadLicensePlateAsync(carId));
    }

    [PostgreSqlFact]
    public async Task Rollback_ReappliesOuterLayer_WithoutChangingPlaintext()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        var carId = Guid.NewGuid();
        await fixture.InsertRawCarAsync(carId, fixture.EncryptOnce("TEST-ROLLBACK-12"));
        var singleLayerCiphertext = await fixture.ReadRawLicensePlateAsync(carId);

        await fixture.RollbackAsync();

        var rolledBackCiphertext = await fixture.ReadRawLicensePlateAsync(carId);
        Assert.Equal(singleLayerCiphertext, DecryptOneLayer(rolledBackCiphertext), StringComparer.Ordinal);
        Assert.Equal("TEST-ROLLBACK-12", await fixture.ReadLicensePlateAsync(carId));
    }

    private static string DecryptOneLayer(string ciphertext)
    {
        var converter = new EncryptedStringConverter(TestKey);
        return (string)converter.ConvertFromProviderExpression.Compile().DynamicInvoke(ciphertext)!;
    }

    private sealed class PostgreSqlFixture : IAsyncDisposable
    {
        private readonly string _adminConnectionString;
        private readonly string _connectionString;
        private readonly DbContextOptions<PakiCarpoolDbContext> _options;
        private readonly IConfiguration _configuration;
        private readonly AesEncryptionHelper _aes;

        private PostgreSqlFixture(string baseConnectionString)
        {
            _adminConnectionString = baseConnectionString;
            DatabaseName = $"paki_plate_{Guid.NewGuid():N}";
            _connectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = DatabaseName
            }.ConnectionString;
            _options = new DbContextOptionsBuilder<PakiCarpoolDbContext>()
                .UseNpgsql(_connectionString)
                .Options;
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AES_KEY_V1"] = TestKey
                })
                .Build();
            _aes = new AesEncryptionHelper(_configuration);
        }

        private string DatabaseName { get; }
        public Guid DriverUserId { get; private set; }
        private Guid DriverProfileId { get; set; }

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

        public PakiCarpoolDbContext CreateContext() => new LicensePlateDbContext(_options, _configuration);

        public DriverService CreateDriverService(PakiCarpoolDbContext context)
        {
            var notifications = new Mock<INotificationService>();
            notifications
                .Setup(service => service.SendInAppNotificationToGroupAsync(
                    It.IsAny<IEnumerable<Guid>>(),
                    It.IsAny<NotificationType>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            var storage = new Mock<IStorageService>();
            storage
                .Setup(service => service.GetCarPhotoUrl(It.IsAny<string>()))
                .Returns<string>(path => $"https://storage.invalid/{path}");

            return new DriverService(
                new UserRepository(context),
                context,
                new Mock<IRideRepository>().Object,
                new Mock<IBookingRepository>().Object,
                notifications.Object,
                new Mock<IFlagService>().Object,
                new Mock<ITrustService>().Object,
                new Mock<IEmailService>().Object,
                _aes,
                storage.Object,
                new Mock<IHubContext<Hub>>().Object);
        }

        public string EncryptOnce(string plaintext) => _aes.Encrypt(plaintext);

        public string EncryptTwice(string plaintext) => _aes.Encrypt(_aes.Encrypt(plaintext));

        public async Task InsertRawCarAsync(Guid carId, string ciphertext)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO "DriverCars"
                    ("Id", "DriverId", "Make", "Model", "Year", "LicensePlate",
                     "AvailableSeats", "Status", "PhotoPaths", "RegDocPath", "CreatedAt")
                VALUES
                    (@id, @driverId, 'Test', 'Car', 2026, @ciphertext,
                     3, 'Pending', '[]', 'fake-registration-path', @createdAt);
                """;
            command.Parameters.AddWithValue("id", carId);
            command.Parameters.AddWithValue("driverId", DriverProfileId);
            command.Parameters.AddWithValue("ciphertext", ciphertext);
            command.Parameters.AddWithValue("createdAt", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<string> ReadRawLicensePlateAsync(Guid carId)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT \"LicensePlate\" FROM \"DriverCars\" WHERE \"Id\" = @id;";
            command.Parameters.AddWithValue("id", carId);
            return (string)(await command.ExecuteScalarAsync())!;
        }

        public async Task<string> ReadLicensePlateAsync(Guid carId)
        {
            await using var context = CreateContext();
            var car = await context.DriverCars.AsNoTracking().SingleAsync(car => car.Id == carId);
            return car.LicensePlate;
        }

        public Task MigrateAsync()
            => RemoveDoubleLicensePlateEncryption.MigrateAsync(
                _connectionString,
                TestKeys,
                activeVersion: 1,
                TestKey);

        public Task RollbackAsync()
            => RemoveDoubleLicensePlateEncryption.RollbackAsync(
                _connectionString,
                TestKeys,
                activeVersion: 1,
                TestKey);

        private async Task InitializeAsync()
        {
            await using (var connection = new NpgsqlConnection(_adminConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE \"{DatabaseName}\"";
                await command.ExecuteNonQueryAsync();
            }

            await using var context = CreateContext();
            await context.Database.EnsureCreatedAsync();
            DriverUserId = Guid.NewGuid();
            DriverProfileId = Guid.NewGuid();
            var user = new User
            {
                Id = DriverUserId,
                FullName = "License Plate Test Driver",
                Email = $"{Guid.NewGuid():N}@example.com",
                Role = UserRole.Driver,
                IsActive = true
            };
            var profile = new DriverProfile
            {
                Id = DriverProfileId,
                UserId = DriverUserId,
                User = user,
                LivenessVerified = true
            };
            context.AddRange(user, profile);
            await context.SaveChangesAsync();
        }

        private sealed class LicensePlateDbContext : PakiCarpoolDbContext
        {
            public LicensePlateDbContext(
                DbContextOptions<PakiCarpoolDbContext> options,
                IConfiguration configuration)
                : base(options, configuration)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
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
