using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Npgsql;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Admin;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.DTOs.Driver;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Repository.Dapper;
using PakiCarpool.Repository.EFCore;
using PakiCarpool.Repository.EFCore.Repositories;
using PakiCarpool.Security.Helpers;
using PakiCarpool.Services.Implementations;
using Xunit;

namespace PakiCarpool.API.Tests;

public sealed class CarSecurityPostgreSqlTests
{
    private const string ConnectionStringEnvironmentVariable = "PAKICARPOOL_TEST_CONNECTIONSTRING";

    [PostgreSqlFact]
    public async Task Register_ValidOwnPaths_BuildsPhotoUrlOnServer_AndReturnsSeats()
    {
        await using var fixture = await CarFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var service = fixture.CreateDriverService(context);
        var photoPath = fixture.Photo(fixture.DriverUserId);

        var response = await service.RegisterCarAsync(
            fixture.DriverUserId,
            fixture.ValidRegistration(photoPaths: [photoPath], availableSeats: 5));

        Assert.True(response.IsSuccess, response.Message);
        Assert.Equal(5, response.Data!.AvailableSeats);
        var car = await fixture.GetCarAsync(response.Data.Id);
        var storedPhotos = JsonSerializer.Deserialize<List<string>>(car.PhotoPaths);
        var storedPhoto = Assert.Single(storedPhotos!);
        Assert.StartsWith("https://storage.invalid/driver-cars/", storedPhoto, StringComparison.Ordinal);
        Assert.DoesNotContain("private://", storedPhoto, StringComparison.Ordinal);
        Assert.Equal(fixture.LastRegistrationPath, car.RegDocPath);

        var status = await service.GetCarStatusAsync(fixture.DriverUserId);
        Assert.Equal(5, Assert.Single(status.Data!).AvailableSeats);
    }

    [PostgreSqlFact]
    public async Task Register_AnotherDriversDocumentOrPhoto_IsRejected()
    {
        await using var fixture = await CarFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var service = fixture.CreateDriverService(context);
        var otherDocument = fixture.Registration(fixture.OtherDriverUserId);
        var otherPhoto = fixture.Photo(fixture.OtherDriverUserId);

        var documentResponse = await service.RegisterCarAsync(
            fixture.DriverUserId,
            fixture.ValidRegistration(registrationPath: otherDocument));
        var photoResponse = await service.RegisterCarAsync(
            fixture.DriverUserId,
            fixture.ValidRegistration(photoPaths: [otherPhoto]));

        AssertValidationError(documentResponse);
        AssertValidationError(photoResponse);
        Assert.Empty(await fixture.GetCarsAsync());
    }

    [PostgreSqlFact]
    public async Task Register_InvalidDocumentPaths_AreRejected()
    {
        await using var fixture = await CarFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var service = fixture.CreateDriverService(context);
        var fileId = Guid.NewGuid();
        var invalidPaths = new[]
        {
            $"private://wrong-bucket/{fixture.DriverUserId:D}/{fileId:N}.jpg",
            $"private://driver-registration-documents/{fixture.DriverUserId:D}/nested/{fileId:N}.jpg",
            $"private://driver-registration-documents/{fixture.DriverUserId:D}/../{fileId:N}.jpg",
            $"/driver-registration-documents/{fixture.DriverUserId:D}/{fileId:N}.jpg",
            $"https://storage.invalid/driver-registration-documents/{fixture.DriverUserId:D}/{fileId:N}.jpg",
            $"private://driver-registration-documents/{fixture.DriverUserId:D}/{fileId:N}.gif",
            $"private://driver-registration-documents/{fixture.DriverUserId:D}/%2e%2e%2f{fileId:N}.jpg",
            $"private://driver-registration-documents/{fixture.DriverUserId:D}\\{fileId:N}.jpg"
        };

        foreach (var invalidPath in invalidPaths)
        {
            var response = await service.RegisterCarAsync(
                fixture.DriverUserId,
                fixture.ValidRegistration(registrationPath: invalidPath));
            AssertValidationError(response);
        }

        Assert.Empty(await fixture.GetCarsAsync());
    }

    [PostgreSqlFact]
    public async Task Register_ExternalAndInvalidPhotoPaths_AreRejected()
    {
        await using var fixture = await CarFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var service = fixture.CreateDriverService(context);
        var fileId = Guid.NewGuid();
        var invalidPaths = new[]
        {
            "https://example.invalid/arbitrary-car-photo.jpg",
            $"private://driver-registration-documents/{fixture.DriverUserId:D}/{fileId:N}.jpg",
            $"private://driver-cars/{fixture.DriverUserId:D}/nested/{fileId:N}.jpg",
            $"private://driver-cars/{fixture.DriverUserId:D}/../{fileId:N}.jpg",
            $"/driver-cars/{fixture.DriverUserId:D}/{fileId:N}.jpg",
            $"private://driver-cars/{fixture.DriverUserId:D}/{fileId:N}.gif",
            $"private://driver-cars/{fixture.DriverUserId:D}/%2e%2e%2f{fileId:N}.jpg"
        };

        foreach (var invalidPath in invalidPaths)
        {
            var response = await service.RegisterCarAsync(
                fixture.DriverUserId,
                fixture.ValidRegistration(photoPaths: [invalidPath]));
            AssertValidationError(response);
        }

        Assert.Empty(await fixture.GetCarsAsync());
    }

    [PostgreSqlFact]
    public async Task Update_AnotherDriversPathAndExternalPhoto_AreRejectedWithoutMutation()
    {
        await using var fixture = await CarFixture.CreateAsync();
        var carId = await fixture.SeedCarAsync(CarStatus.Pending);
        var original = await fixture.GetCarAsync(carId);
        await using var context = fixture.CreateContext();
        var service = fixture.CreateDriverService(context);

        var documentResponse = await service.UpdateCarAsync(
            fixture.DriverUserId,
            carId,
            new UpdateCarDto { RegDocPath = fixture.Registration(fixture.OtherDriverUserId) });
        var otherPhotoResponse = await service.UpdateCarAsync(
            fixture.DriverUserId,
            carId,
            new UpdateCarDto { PhotoPaths = [fixture.Photo(fixture.OtherDriverUserId)] });
        var externalPhotoResponse = await service.UpdateCarAsync(
            fixture.DriverUserId,
            carId,
            new UpdateCarDto { PhotoPaths = ["https://example.invalid/external.jpg"] });

        AssertValidationError(documentResponse);
        AssertValidationError(otherPhotoResponse);
        AssertValidationError(externalPhotoResponse);
        var unchanged = await fixture.GetCarAsync(carId);
        Assert.Equal(original.RegDocPath, unchanged.RegDocPath);
        Assert.Equal(original.PhotoPaths, unchanged.PhotoPaths);
        Assert.Equal(original.Status, unchanged.Status);
    }

    [PostgreSqlFact]
    public async Task ApprovedEdit_CreatesPendingReplacement_AndLeavesOriginalApproved()
    {
        await using var fixture = await CarFixture.CreateAsync();
        var originalId = await fixture.SeedCarAsync(CarStatus.Approved, availableSeats: 4);
        var original = await fixture.GetCarAsync(originalId);
        await using var context = fixture.CreateContext();
        var service = fixture.CreateDriverService(context);

        var response = await service.UpdateCarAsync(
            fixture.DriverUserId,
            originalId,
            new UpdateCarDto
            {
                Make = "Replacement",
                AvailableSeats = 2,
                PhotoPaths = [fixture.Photo(fixture.DriverUserId)],
                RegDocPath = fixture.Registration(fixture.DriverUserId)
            });

        Assert.True(response.IsSuccess, response.Message);
        Assert.NotEqual(originalId, response.Data!.Id);
        Assert.Equal(2, response.Data.AvailableSeats);
        var replacement = await fixture.GetCarAsync(response.Data.Id);
        var unchangedOriginal = await fixture.GetCarAsync(originalId);
        Assert.Equal(CarStatus.Pending, replacement.Status);
        Assert.Equal(originalId, replacement.ReplacesCarId);
        Assert.Equal("Replacement", replacement.Make);
        Assert.Equal(2, replacement.AvailableSeats);
        Assert.Contains("https://storage.invalid/driver-cars/", replacement.PhotoPaths, StringComparison.Ordinal);
        Assert.Equal(CarStatus.Approved, unchangedOriginal.Status);
        Assert.Null(unchangedOriginal.ReplacedByCarId);
        Assert.Equal(original.Make, unchangedOriginal.Make);
        Assert.Equal(original.AvailableSeats, unchangedOriginal.AvailableSeats);
        Assert.Equal(original.PhotoPaths, unchangedOriginal.PhotoPaths);
        Assert.Equal(original.RegDocPath, unchangedOriginal.RegDocPath);
        Assert.Equal(original.ApprovedAt, unchangedOriginal.ApprovedAt);
    }

    [PostgreSqlFact]
    public async Task SecondApprovedEdit_UpdatesSamePendingReplacement()
    {
        await using var fixture = await CarFixture.CreateAsync();
        var originalId = await fixture.SeedCarAsync(CarStatus.Approved);
        await using var context = fixture.CreateContext();
        var service = fixture.CreateDriverService(context);
        var first = await service.UpdateCarAsync(
            fixture.DriverUserId,
            originalId,
            new UpdateCarDto { Model = "First edit" });

        var second = await service.UpdateCarAsync(
            fixture.DriverUserId,
            originalId,
            new UpdateCarDto { Model = "Second edit", AvailableSeats = 6 });

        Assert.True(first.IsSuccess, first.Message);
        Assert.True(second.IsSuccess, second.Message);
        Assert.Equal(first.Data!.Id, second.Data!.Id);
        Assert.Equal(6, second.Data.AvailableSeats);
        Assert.Equal(2, (await fixture.GetCarsAsync()).Count);
        var replacement = await fixture.GetCarAsync(first.Data.Id);
        Assert.Equal("Second edit", replacement.Model);
        Assert.Equal(6, replacement.AvailableSeats);
    }

    [PostgreSqlFact]
    public async Task ReplacementEdit_AnotherDriversDocument_IsRejected()
    {
        await using var fixture = await CarFixture.CreateAsync();
        var originalId = await fixture.SeedCarAsync(CarStatus.Approved);
        await using var context = fixture.CreateContext();
        var service = fixture.CreateDriverService(context);
        var created = await service.UpdateCarAsync(
            fixture.DriverUserId,
            originalId,
            new UpdateCarDto { RegDocPath = fixture.Registration(fixture.DriverUserId) });
        var before = await fixture.GetCarAsync(created.Data!.Id);

        var rejected = await service.UpdateCarAsync(
            fixture.DriverUserId,
            originalId,
            new UpdateCarDto { RegDocPath = fixture.Registration(fixture.OtherDriverUserId) });

        AssertValidationError(rejected);
        var after = await fixture.GetCarAsync(before.Id);
        Assert.Equal(before.RegDocPath, after.RegDocPath);
        Assert.Equal(2, (await fixture.GetCarsAsync()).Count);
    }

    [PostgreSqlFact]
    public async Task Database_AllowsOnlyOnePendingReplacementPerOriginal()
    {
        await using var fixture = await CarFixture.CreateAsync();
        var originalId = await fixture.SeedCarAsync(CarStatus.Approved);
        await using (var context = fixture.CreateContext())
        {
            var service = fixture.CreateDriverService(context);
            var response = await service.UpdateCarAsync(
                fixture.DriverUserId,
                originalId,
                new UpdateCarDto { Make = "Pending replacement" });
            Assert.True(response.IsSuccess, response.Message);
        }

        await using var duplicateContext = fixture.CreateContext();
        await duplicateContext.DriverCars.AddAsync(fixture.CreateReplacementRow(originalId));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
    }

    [PostgreSqlFact]
    public async Task Approval_RetiresOriginalForNewRides_ButActiveRideIsUntouched()
    {
        await using var fixture = await CarFixture.CreateAsync();
        var originalId = await fixture.SeedCarAsync(CarStatus.Approved);
        var rideId = await fixture.SeedRideAsync(originalId, RideStatus.Active);
        var rideBefore = await fixture.GetRideAsync(rideId);
        string replacementId;
        await using (var context = fixture.CreateContext())
        {
            var driver = fixture.CreateDriverService(context);
            var response = await driver.UpdateCarAsync(
                fixture.DriverUserId,
                originalId,
                new UpdateCarDto { Make = "Reviewed replacement" });
            replacementId = response.Data!.Id.ToString();
        }

        await using (var context = fixture.CreateContext())
        {
            var admin = fixture.CreateAdminService(context);
            var response = await admin.ApproveCarAsync(Guid.Parse(replacementId));
            Assert.True(response.IsSuccess, response.Message);
        }

        var replacement = await fixture.GetCarAsync(Guid.Parse(replacementId));
        var original = await fixture.GetCarAsync(originalId);
        var rideAfter = await fixture.GetRideAsync(rideId);
        Assert.Equal(CarStatus.Approved, replacement.Status);
        Assert.Equal(CarStatus.Approved, original.Status);
        Assert.Equal(replacement.Id, original.ReplacedByCarId);
        Assert.Equal(originalId, rideAfter.CarId);
        Assert.Equal(RideStatus.Active, rideAfter.Status);
        Assert.Equal(rideBefore.StartedAt, rideAfter.StartedAt);
        Assert.Equal(rideBefore.MaxSeats, rideAfter.MaxSeats);
        Assert.Equal(rideBefore.CompletedAt, rideAfter.CompletedAt);
    }

    [PostgreSqlFact]
    public async Task Rejection_LeavesOriginalApprovedAndUnchanged()
    {
        await using var fixture = await CarFixture.CreateAsync();
        var originalId = await fixture.SeedCarAsync(CarStatus.Approved);
        var originalBefore = await fixture.GetCarAsync(originalId);
        Guid replacementId;
        await using (var context = fixture.CreateContext())
        {
            var driver = fixture.CreateDriverService(context);
            var response = await driver.UpdateCarAsync(
                fixture.DriverUserId,
                originalId,
                new UpdateCarDto { Make = "Rejected replacement" });
            replacementId = response.Data!.Id;
        }

        await using (var context = fixture.CreateContext())
        {
            var admin = fixture.CreateAdminService(context);
            var response = await admin.RejectCarAsync(
                replacementId,
                new RejectCarDto { RejectionReason = "Evidence needs review." });
            Assert.True(response.IsSuccess, response.Message);
        }

        var replacement = await fixture.GetCarAsync(replacementId);
        var originalAfter = await fixture.GetCarAsync(originalId);
        Assert.Equal(CarStatus.Rejected, replacement.Status);
        Assert.Equal(CarStatus.Approved, originalAfter.Status);
        Assert.Null(originalAfter.ReplacedByCarId);
        Assert.Equal(originalBefore.ApprovedAt, originalAfter.ApprovedAt);
        Assert.Equal(originalBefore.Make, originalAfter.Make);
        Assert.Equal(originalBefore.PhotoPaths, originalAfter.PhotoPaths);
        Assert.Equal(originalBefore.RegDocPath, originalAfter.RegDocPath);
    }

    [PostgreSqlFact]
    public async Task NewRide_UsesApprovedOriginal_NotPendingReplacement()
    {
        await using var fixture = await CarFixture.CreateAsync();
        var originalId = await fixture.SeedCarAsync(CarStatus.Approved);
        await using var context = fixture.CreateContext();
        var driver = fixture.CreateDriverService(context);
        var replacement = await driver.UpdateCarAsync(
            fixture.DriverUserId,
            originalId,
            new UpdateCarDto { Make = "Still pending" });

        var ride = await driver.PostRideAsync(fixture.DriverUserId, fixture.ValidRide());

        Assert.True(ride.IsSuccess, ride.Message);
        var storedRide = await fixture.GetLatestRideAsync();
        Assert.Equal(originalId, storedRide.CarId);
        Assert.NotEqual(replacement.Data!.Id, storedRide.CarId);
        Assert.Equal(CarStatus.Pending, (await fixture.GetCarAsync(replacement.Data.Id)).Status);
    }

    [PostgreSqlFact]
    public async Task NewRide_UsesApprovedReplacement_NotRetiredOriginal()
    {
        await using var fixture = await CarFixture.CreateAsync();
        var originalId = await fixture.SeedCarAsync(CarStatus.Approved);
        Guid replacementId;
        await using (var context = fixture.CreateContext())
        {
            var driver = fixture.CreateDriverService(context);
            var replacement = await driver.UpdateCarAsync(
                fixture.DriverUserId,
                originalId,
                new UpdateCarDto { Make = "Approved replacement", AvailableSeats = 5 });
            replacementId = replacement.Data!.Id;
        }
        await using (var context = fixture.CreateContext())
        {
            var admin = fixture.CreateAdminService(context);
            var approval = await admin.ApproveCarAsync(replacementId);
            Assert.True(approval.IsSuccess, approval.Message);
        }

        await using (var context = fixture.CreateContext())
        {
            var driver = fixture.CreateDriverService(context);
            var ride = await driver.PostRideAsync(fixture.DriverUserId, fixture.ValidRide(maxSeats: 5));
            Assert.True(ride.IsSuccess, ride.Message);
        }

        var storedRide = await fixture.GetLatestRideAsync();
        Assert.Equal(replacementId, storedRide.CarId);
        Assert.NotEqual(originalId, storedRide.CarId);
        Assert.Equal(5, storedRide.MaxSeats);
    }

    private static void AssertValidationError(ApiResponse<CarStatusResponseDto> response)
    {
        Assert.False(response.IsSuccess);
        Assert.Equal(ErrorCodes.ValidationError, response.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(response.Message));
    }

    private sealed class CarFixture : IAsyncDisposable
    {
        private static readonly string TestKey = Convert.ToBase64String(new byte[32]);
        private readonly string _adminConnectionString;
        private readonly string _connectionString;
        private readonly DbContextOptions<PakiCarpoolDbContext> _options;
        private readonly IConfiguration _configuration;
        private readonly AesEncryptionHelper _aes;

        private CarFixture(string baseConnectionString)
        {
            _adminConnectionString = baseConnectionString;
            DatabaseName = $"paki_car_security_{Guid.NewGuid():N}";
            _connectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = DatabaseName
            }.ConnectionString;
            _options = new DbContextOptionsBuilder<PakiCarpoolDbContext>()
                .UseNpgsql(_connectionString)
                .Options;
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AES_KEY_V1"] = TestKey })
                .Build();
            _aes = new AesEncryptionHelper(_configuration);
        }

        private string DatabaseName { get; }
        public Guid DriverUserId { get; private set; }
        public Guid OtherDriverUserId { get; private set; }
        public Guid DriverProfileId { get; private set; }
        public Guid OtherDriverProfileId { get; private set; }
        public string LastRegistrationPath { get; private set; } = string.Empty;

        public static async Task<CarFixture> CreateAsync()
        {
            var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
                ?? throw new InvalidOperationException("PostgreSQL test connection string is missing.");
            var fixture = new CarFixture(connectionString);
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

        public PakiCarpoolDbContext CreateContext() => new CarDbContext(_options, _configuration);

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
            var storage = CreateStorageMock();
            var rides = new RideRepository(
                context,
                new RideSearchQuery(new NpgsqlConnectionFactory(_connectionString)));
            return new DriverService(
                new UserRepository(context),
                context,
                rides,
                new Mock<IBookingRepository>().Object,
                notifications.Object,
                new Mock<IFlagService>().Object,
                new Mock<ITrustService>().Object,
                new Mock<IEmailService>().Object,
                _aes,
                storage.Object,
                new Mock<IHubContext<Hub>>().Object);
        }

        public AdminService CreateAdminService(PakiCarpoolDbContext context) =>
            new(
                new UserRepository(context),
                context,
                new Mock<IRideRepository>().Object,
                new Mock<IBookingRepository>().Object,
                new Mock<IAdminRepository>().Object,
                new Mock<INotificationService>().Object,
                new Mock<IEmailService>().Object,
                CreateStorageMock().Object);

        public RegisterCarDto ValidRegistration(
            string[]? photoPaths = null,
            string? registrationPath = null,
            int availableSeats = 4)
        {
            LastRegistrationPath = registrationPath ?? Registration(DriverUserId);
            return new RegisterCarDto
            {
                Make = "Test",
                Model = "Car",
                Year = 2026,
                LicensePlate = "TEST-CAR-16",
                AvailableSeats = availableSeats,
                PhotoPaths = (photoPaths ?? [Photo(DriverUserId)]).ToList(),
                RegDocPath = LastRegistrationPath
            };
        }

        public PostRideDto ValidRide(int maxSeats = 2) => new()
        {
            MaxSeats = maxSeats,
            Fare = 100,
            Stops =
            [
                new RideStopDto { Name = "Start", Lat = 24.86, Lng = 67.00, SequenceOrder = 1 },
                new RideStopDto { Name = "End", Lat = 24.87, Lng = 67.01, SequenceOrder = 2 }
            ]
        };

        public string Photo(Guid ownerId) =>
            $"private://driver-cars/{ownerId:D}/{Guid.NewGuid():N}.jpg";

        public string Registration(Guid ownerId) =>
            $"private://driver-registration-documents/{ownerId:D}/{Guid.NewGuid():N}.png";

        public async Task<Guid> SeedCarAsync(CarStatus status, int availableSeats = 4)
        {
            var carId = Guid.NewGuid();
            await using var context = CreateContext();
            await context.DriverCars.AddAsync(new DriverCar
            {
                Id = carId,
                DriverId = DriverProfileId,
                Make = "Original",
                Model = "Car",
                Year = 2025,
                LicensePlate = "ORIGINAL-16",
                AvailableSeats = availableSeats,
                Status = status,
                ApprovedAt = status == CarStatus.Approved ? DateTime.UtcNow.AddDays(-1) : null,
                PhotoPaths = JsonSerializer.Serialize(new[] { "https://storage.invalid/driver-cars/original.jpg" }),
                RegDocPath = Registration(DriverUserId),
                CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            });
            await context.SaveChangesAsync();
            return carId;
        }

        public DriverCar CreateReplacementRow(Guid originalId) => new()
        {
            Id = Guid.NewGuid(),
            DriverId = DriverProfileId,
            Make = "Duplicate",
            Model = "Replacement",
            Year = 2026,
            LicensePlate = "DUPLICATE-16",
            AvailableSeats = 3,
            Status = CarStatus.Pending,
            PhotoPaths = "[]",
            RegDocPath = Registration(DriverUserId),
            ReplacesCarId = originalId,
            CreatedAt = DateTime.UtcNow
        };

        public async Task<Guid> SeedRideAsync(Guid carId, RideStatus status)
        {
            var rideId = Guid.NewGuid();
            await using var context = CreateContext();
            await context.Rides.AddAsync(new Ride
            {
                Id = rideId,
                DriverId = DriverProfileId,
                CarId = carId,
                Fare = 100,
                MaxSeats = 2,
                Status = status,
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                CancelGraceExpiresAt = DateTime.UtcNow.AddMinutes(-5),
                StartedAt = status == RideStatus.Active ? DateTime.UtcNow.AddMinutes(-5) : null
            });
            await context.SaveChangesAsync();
            return rideId;
        }

        public async Task<DriverCar> GetCarAsync(Guid id)
        {
            await using var context = CreateContext();
            return await context.DriverCars.AsNoTracking().SingleAsync(car => car.Id == id);
        }

        public async Task<List<DriverCar>> GetCarsAsync()
        {
            await using var context = CreateContext();
            return await context.DriverCars.AsNoTracking().ToListAsync();
        }

        public async Task<Ride> GetRideAsync(Guid id)
        {
            await using var context = CreateContext();
            return await context.Rides.AsNoTracking().SingleAsync(ride => ride.Id == id);
        }

        public async Task<Ride> GetLatestRideAsync()
        {
            await using var context = CreateContext();
            return await context.Rides.AsNoTracking()
                .OrderByDescending(ride => ride.CreatedAt)
                .FirstAsync();
        }

        private static Mock<IStorageService> CreateStorageMock()
        {
            var storage = new Mock<IStorageService>();
            storage
                .Setup(service => service.GetCarPhotoUrl(It.IsAny<string>()))
                .Returns<string>(path =>
                    $"https://storage.invalid/{path["private://".Length..]}");
            return storage;
        }

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
            OtherDriverUserId = Guid.NewGuid();
            DriverProfileId = Guid.NewGuid();
            OtherDriverProfileId = Guid.NewGuid();
            var driver = new User
            {
                Id = DriverUserId,
                FullName = "Car Security Driver",
                Email = $"{Guid.NewGuid():N}@example.invalid",
                Role = UserRole.Driver,
                IsActive = true
            };
            var otherDriver = new User
            {
                Id = OtherDriverUserId,
                FullName = "Other Driver",
                Email = $"{Guid.NewGuid():N}@example.invalid",
                Role = UserRole.Driver,
                IsActive = true
            };
            context.AddRange(
                driver,
                otherDriver,
                new DriverProfile
                {
                    Id = DriverProfileId,
                    UserId = DriverUserId,
                    LivenessVerified = true
                },
                new DriverProfile
                {
                    Id = OtherDriverProfileId,
                    UserId = OtherDriverUserId,
                    LivenessVerified = true
                });
            await context.SaveChangesAsync();
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

        private sealed class CarDbContext(
            DbContextOptions<PakiCarpoolDbContext> options,
            IConfiguration configuration) : PakiCarpoolDbContext(options, configuration);

        private sealed class NpgsqlConnectionFactory(string connectionString) : IDapperConnectionFactory
        {
            public IDbConnection CreateConnection() => new NpgsqlConnection(connectionString);
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
