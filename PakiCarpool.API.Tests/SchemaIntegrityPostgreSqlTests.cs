using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using PakiCarpool.API.Controllers;
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

public sealed class SchemaIntegrityPostgreSqlTests
{
    private const string ConnectionStringEnvironmentVariable = "PAKICARPOOL_TEST_CONNECTIONSTRING";

    [PostgreSqlFact]
    public async Task DuplicateEmail_WithDifferentCase_IsRejected()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        var first = fixture.CreateUser("Case.Person@example.com");
        first.Id = Guid.NewGuid();
        await using (var context = fixture.CreateContext())
        {
            context.Users.Add(first);
            await context.SaveChangesAsync();
        }

        var duplicate = fixture.CreateUser("case.person@EXAMPLE.COM");
        duplicate.Id = Guid.NewGuid();
        await using var duplicateContext = fixture.CreateContext();
        duplicateContext.Users.Add(duplicate);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => duplicateContext.SaveChangesAsync());

        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("IX_Users_Email_Lower", postgres.ConstraintName);
    }

    [PostgreSqlFact]
    public async Task RatingStars_CheckRejectsInvalidValues_AndAcceptsBoundaries()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();

        await fixture.ExpectViolationAsync(
            fixture.RatingSql(0), PostgresErrorCodes.CheckViolation, "CK_Ratings_Stars");
        await fixture.ExpectViolationAsync(
            fixture.RatingSql(6), PostgresErrorCodes.CheckViolation, "CK_Ratings_Stars");

        await fixture.ExecuteAsync(fixture.RatingSql(1));
        await fixture.ExecuteAsync(fixture.RatingSql(5, reverseDirection: true));
    }

    [PostgreSqlFact]
    public async Task FareAndSeatChecks_RejectInvalidValues_AndAcceptBoundaries()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();

        await fixture.ExpectViolationAsync(
            $"""UPDATE "Rides" SET "Fare" = -0.01 WHERE "Id" = '{fixture.Ride.Id}';""",
            PostgresErrorCodes.CheckViolation,
            "CK_Rides_Fare");
        await fixture.ExecuteAsync($"""UPDATE "Rides" SET "Fare" = 0 WHERE "Id" = '{fixture.Ride.Id}';""");

        foreach (var seats in new[] { 0, 7 })
        {
            await fixture.ExpectViolationAsync(
                $"""UPDATE "Rides" SET "MaxSeats" = {seats} WHERE "Id" = '{fixture.Ride.Id}';""",
                PostgresErrorCodes.CheckViolation,
                "CK_Rides_MaxSeats");
            await fixture.ExpectViolationAsync(
                $"""UPDATE "DriverCars" SET "AvailableSeats" = {seats} WHERE "Id" = '{fixture.Car.Id}';""",
                PostgresErrorCodes.CheckViolation,
                "CK_DriverCars_AvailableSeats");
        }

        foreach (var seats in new[] { 1, 6 })
        {
            await fixture.ExecuteAsync($"""UPDATE "Rides" SET "MaxSeats" = {seats} WHERE "Id" = '{fixture.Ride.Id}';""");
            await fixture.ExecuteAsync($"""UPDATE "DriverCars" SET "AvailableSeats" = {seats} WHERE "Id" = '{fixture.Car.Id}';""");
        }
    }

    [PostgreSqlFact]
    public async Task CoordinateChecks_RejectInvalidValues_AndAcceptBoundaries()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        fixture.LocationId = Guid.NewGuid();
        await fixture.ExecuteAsync($"""
            INSERT INTO "DriverLocations" ("Id", "DriverId", "RideId", "Latitude", "Longitude", "RecordedAt")
            VALUES ('{fixture.LocationId}', '{fixture.Profile.Id}', '{fixture.Ride.Id}', 0, 0, NOW());
            """);

        await fixture.CheckCoordinateAsync("RideStops", fixture.Pickup.Id, "Latitude", -90, 90, "CK_RideStops_Latitude");
        await fixture.CheckCoordinateAsync("RideStops", fixture.Pickup.Id, "Longitude", -180, 180, "CK_RideStops_Longitude");
        await fixture.CheckCoordinateAsync("DriverLocations", fixture.LocationId, "Latitude", -90, 90, "CK_DriverLocations_Latitude");
        await fixture.CheckCoordinateAsync("DriverLocations", fixture.LocationId, "Longitude", -180, 180, "CK_DriverLocations_Longitude");
    }

    [PostgreSqlFact]
    public async Task RequiredForeignKeys_RejectOrphans()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();

        foreach (var (constraintName, sql) in fixture.OrphanInsertStatements())
        {
            await fixture.ExpectViolationAsync(sql, PostgresErrorCodes.ForeignKeyViolation, constraintName);
        }
    }

    [PostgreSqlFact]
    public async Task Registration_WithExistingEmailDifferentCase_ReturnsClean400()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var configuration = fixture.CreateAuthConfiguration();
        var emailService = new Mock<IEmailService>();
        emailService.Setup(service => service.SendEmailOtpAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        var authService = new AuthService(
            new UserRepository(context),
            new Mock<IUserSessionService>().Object,
            new Mock<IRideRepository>().Object,
            emailService.Object,
            new JwtHelper(configuration),
            new AesEncryptionHelper(configuration),
            new Mock<IOAuthStateStore>().Object,
            new Mock<IHttpClientFactory>().Object,
            configuration,
            new Mock<IHCaptchaService>().Object,
            NullLogger<AuthService>.Instance);
        var controller = new AuthController(
            authService,
            configuration,
            new Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().Object);

        var firstResult = await controller.Register(new RegisterRequestDto
        {
            FullName = "First User",
            Email = "Existing.Person@example.com",
            Password = "StrongPass1!",
            Role = UserRole.Passenger
        });
        Assert.Equal(200, Assert.IsType<OkObjectResult>(firstResult).StatusCode);

        var duplicateResult = await controller.Register(new RegisterRequestDto
        {
            FullName = "Second User",
            Email = "existing.person@EXAMPLE.COM",
            Password = "StrongPass1!",
            Role = UserRole.Passenger
        });

        var objectResult = Assert.IsType<ObjectResult>(duplicateResult);
        Assert.Equal(400, objectResult.StatusCode);
        var error = Assert.IsType<ErrorResponse>(objectResult.Value);
        Assert.Equal(400, error.StatusCode);
        Assert.Equal("An account with this email already exists.", error.Message);
        Assert.Equal(1, await context.Users.CountAsync(user => user.Email == "existing.person@example.com"));
        emailService.Verify(service => service.SendEmailOtpAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [PostgreSqlFact]
    public async Task CustomPartialIndexes_ArePresentWithoutRemovingOrdinaryIndex()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        var indexNames = await fixture.GetIndexNamesAsync();

        Assert.Contains("ix_bookings_active_passenger", indexNames);
        Assert.Contains("ix_rides_active_driver", indexNames);
        Assert.Contains("IX_Bookings_PassengerId", indexNames);
    }

    private sealed class PostgreSqlFixture : IAsyncDisposable
    {
        private readonly string _adminConnectionString;
        private readonly string _connectionString;
        private readonly DbContextOptions<PakiCarpoolDbContext> _options;

        private PostgreSqlFixture(string baseConnectionString)
        {
            _adminConnectionString = baseConnectionString;
            DatabaseName = $"paki_schema_integrity_{Guid.NewGuid():N}";
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
        public User DriverUser { get; private set; } = null!;
        public User Passenger { get; private set; } = null!;
        public DriverProfile Profile { get; private set; } = null!;
        public DriverCar Car { get; private set; } = null!;
        public Ride Ride { get; private set; } = null!;
        public RideStop Pickup { get; private set; } = null!;
        public RideStop Dropoff { get; private set; } = null!;
        public Booking Booking { get; private set; } = null!;
        public Guid LocationId { get; set; }

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

        public User CreateUser(string email) => new()
        {
            FullName = "Schema Integrity Test User",
            Email = email,
            Role = UserRole.Passenger,
            IsEmailVerified = true,
            CreatedAt = DateTime.UtcNow
        };

        public IConfiguration CreateAuthConfiguration()
            => new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JWT_SECRET"] = new string('s', 64),
                    ["JWT_ISSUER"] = "schema-integrity-tests",
                    ["JWT_AUDIENCE"] = "schema-integrity-test-clients",
                    ["AES_KEY_V1"] = Convert.ToBase64String(new byte[32]),
                    ["Auth:EmailOtpExpiryMinutes"] = "10"
                })
                .Build();

        public string RatingSql(int stars, bool reverseDirection = false)
        {
            var rater = reverseDirection ? Passenger.Id : DriverUser.Id;
            var ratee = reverseDirection ? DriverUser.Id : Passenger.Id;
            return $"""
                INSERT INTO "Ratings" ("Id", "RideId", "RaterId", "RateeId", "Stars", "CreatedAt")
                VALUES ('{Guid.NewGuid()}', '{Ride.Id}', '{rater}', '{ratee}', {stars}, NOW());
                """;
        }

        public async Task CheckCoordinateAsync(
            string table,
            Guid rowId,
            string column,
            int minimum,
            int maximum,
            string constraintName)
        {
            foreach (var invalid in new[] { $"{minimum}.0001", $"{maximum}.0001", "'NaN'" })
            {
                await ExpectViolationAsync(
                    $"""UPDATE "{table}" SET "{column}" = {invalid} WHERE "Id" = '{rowId}';""",
                    PostgresErrorCodes.CheckViolation,
                    constraintName);
            }

            await ExecuteAsync($"""UPDATE "{table}" SET "{column}" = {minimum} WHERE "Id" = '{rowId}';""");
            await ExecuteAsync($"""UPDATE "{table}" SET "{column}" = {maximum} WHERE "Id" = '{rowId}';""");
        }

        public IEnumerable<(string ConstraintName, string Sql)> OrphanInsertStatements()
        {
            var orphan = Guid.NewGuid();
            yield return ("FK_DriverLocations_DriverProfiles_DriverId", $"""
                INSERT INTO "DriverLocations" ("Id", "DriverId", "RideId", "Latitude", "Longitude", "RecordedAt")
                VALUES ('{Guid.NewGuid()}', '{orphan}', '{Ride.Id}', 0, 0, NOW());
                """);
            yield return ("FK_DriverLocations_Rides_RideId", $"""
                INSERT INTO "DriverLocations" ("Id", "DriverId", "RideId", "Latitude", "Longitude", "RecordedAt")
                VALUES ('{Guid.NewGuid()}', '{Profile.Id}', '{orphan}', 0, 0, NOW());
                """);
            yield return ("FK_Ratings_Rides_RideId", $"""
                INSERT INTO "Ratings" ("Id", "RideId", "RaterId", "RateeId", "Stars", "CreatedAt")
                VALUES ('{Guid.NewGuid()}', '{orphan}', '{DriverUser.Id}', '{Passenger.Id}', 5, NOW());
                """);
            yield return ("FK_Ratings_Users_RaterId", $"""
                INSERT INTO "Ratings" ("Id", "RideId", "RaterId", "RateeId", "Stars", "CreatedAt")
                VALUES ('{Guid.NewGuid()}', '{Ride.Id}', '{orphan}', '{Passenger.Id}', 5, NOW());
                """);
            yield return ("FK_Ratings_Users_RateeId", $"""
                INSERT INTO "Ratings" ("Id", "RideId", "RaterId", "RateeId", "Stars", "CreatedAt")
                VALUES ('{Guid.NewGuid()}', '{Ride.Id}', '{DriverUser.Id}', '{orphan}', 5, NOW());
                """);

            foreach (var (constraint, column) in new[]
                     {
                         ("FK_Flags_Users_ReporterId", "ReporterId"),
                         ("FK_Flags_Users_ReporteeId", "ReporteeId"),
                         ("FK_Flags_Rides_RideId", "RideId")
                     })
            {
                var reporter = column == "ReporterId" ? orphan : DriverUser.Id;
                var reportee = column == "ReporteeId" ? orphan : Passenger.Id;
                var ride = column == "RideId" ? orphan : Ride.Id;
                yield return (constraint, $"""
                    INSERT INTO "Flags" ("Id", "ReporterId", "ReporteeId", "RideId", "Reason", "Status", "CreatedAt")
                    VALUES ('{Guid.NewGuid()}', '{reporter}', '{reportee}', '{ride}', 'orphan test', 'Open', NOW());
                    """);
            }

            foreach (var (constraint, column) in new[]
                     {
                         ("FK_NoShowLogs_Users_ReporterId", "ReporterId"),
                         ("FK_NoShowLogs_Users_OffenderId", "OffenderId"),
                         ("FK_NoShowLogs_Rides_RideId", "RideId"),
                         ("FK_NoShowLogs_Bookings_BookingId", "BookingId")
                     })
            {
                var reporter = column == "ReporterId" ? orphan : DriverUser.Id;
                var offender = column == "OffenderId" ? orphan : Passenger.Id;
                var ride = column == "RideId" ? orphan : Ride.Id;
                var booking = column == "BookingId" ? $"'{orphan}'" : "NULL";
                yield return (constraint, $"""
                    INSERT INTO "NoShowLogs" ("Id", "ReporterId", "OffenderId", "RideId", "BookingId", "CreatedAt")
                    VALUES ('{Guid.NewGuid()}', '{reporter}', '{offender}', '{ride}', {booking}, NOW());
                    """);
            }

            yield return ("FK_CancellationLogs_Users_UserId", $"""
                INSERT INTO "CancellationLogs" ("Id", "UserId", "EntityId", "EntityType", "Reason", "Classification", "TrustPointsDeducted", "CreatedAt")
                VALUES ('{Guid.NewGuid()}', '{orphan}', '{Ride.Id}', 'Ride', 'orphan test', 'Frivolous', 5, NOW());
                """);

            foreach (var (constraint, column) in new[]
                     {
                         ("FK_ChatMessages_Users_SenderId", "SenderId"),
                         ("FK_ChatMessages_Users_ReceiverId", "ReceiverId")
                     })
            {
                var sender = column == "SenderId" ? orphan : DriverUser.Id;
                var receiver = column == "ReceiverId" ? orphan : Passenger.Id;
                yield return (constraint, $"""
                    INSERT INTO "ChatMessages" ("Id", "BookingId", "SenderId", "ReceiverId", "Message", "SentAt", "ExpiresAt", "IsDelivered")
                    VALUES ('{Guid.NewGuid()}', '{Booking.Id}', '{sender}', '{receiver}', 'orphan test', NOW(), NOW() + INTERVAL '7 days', FALSE);
                    """);
            }
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        public async Task ExpectViolationAsync(string sql, string sqlState, string constraintName)
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql));
            Assert.Equal(sqlState, exception.SqlState);
            Assert.Equal(constraintName, exception.ConstraintName);
        }

        public async Task<List<string>> GetIndexNamesAsync()
        {
            var names = new List<string>();
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT indexname FROM pg_indexes WHERE schemaname = 'public';";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                names.Add(reader.GetString(0));
            return names;
        }

        private async Task InitializeAsync()
        {
            await using (var connection = new NpgsqlConnection(_adminConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"""CREATE DATABASE "{DatabaseName}";""";
                await command.ExecuteNonQueryAsync();
            }

            await using (var context = CreateContext())
            {
                await context.Database.EnsureCreatedAsync();
            }

            // Expression indexes cannot be represented by the EF model; this is the
            // same additive statement used by AddSchemaIntegritySafeguards.Up().
            await ExecuteAsync("""
                CREATE UNIQUE INDEX "IX_Users_Email_Lower"
                ON "Users" (LOWER("Email"));
                """);
            await SeedAsync();
        }

        private async Task SeedAsync()
        {
            var now = DateTime.UtcNow;
            DriverUser = CreateUser("driver@example.com");
            DriverUser.Id = Guid.NewGuid();
            DriverUser.Role = UserRole.Driver;
            Passenger = CreateUser("passenger@example.com");
            Passenger.Id = Guid.NewGuid();
            Profile = new DriverProfile
            {
                Id = Guid.NewGuid(),
                UserId = DriverUser.Id,
                User = DriverUser,
                AverageRating = 0,
                CreatedAt = now
            };
            Car = new DriverCar
            {
                Id = Guid.NewGuid(),
                DriverId = Profile.Id,
                Driver = Profile,
                Make = "Test",
                Model = "Car",
                Year = 2026,
                LicensePlate = "TEST-ITEM13",
                AvailableSeats = 2,
                Status = CarStatus.Approved,
                PhotoPaths = "[]",
                RegDocPath = "private://test/document.jpg",
                CreatedAt = now
            };
            Ride = new Ride
            {
                Id = Guid.NewGuid(),
                DriverId = Profile.Id,
                Driver = Profile,
                CarId = Car.Id,
                Car = Car,
                Fare = 100,
                MaxSeats = 2,
                Status = RideStatus.Completed,
                CreatedAt = now,
                CancelGraceExpiresAt = now.AddMinutes(5),
                CompletedAt = now
            };
            Pickup = new RideStop
            {
                Id = Guid.NewGuid(),
                RideId = Ride.Id,
                Ride = Ride,
                Name = "Pickup",
                Latitude = 24.8,
                Longitude = 67.0,
                SequenceOrder = 1
            };
            Dropoff = new RideStop
            {
                Id = Guid.NewGuid(),
                RideId = Ride.Id,
                Ride = Ride,
                Name = "Dropoff",
                Latitude = 24.9,
                Longitude = 67.1,
                SequenceOrder = 2
            };
            Ride.Stops = new List<RideStop> { Pickup, Dropoff };
            Booking = new Booking
            {
                Id = Guid.NewGuid(),
                RideId = Ride.Id,
                Ride = Ride,
                PassengerId = Passenger.Id,
                Passenger = Passenger,
                PickupStopId = Pickup.Id,
                PickupStop = Pickup,
                DropoffStopId = Dropoff.Id,
                DropoffStop = Dropoff,
                Status = BookingStatus.Completed,
                RequestedAt = now,
                CompletedAt = now,
                IsCashPaid = true
            };

            await using var context = CreateContext();
            context.AddRange(DriverUser, Passenger, Profile, Car, Ride, Pickup, Dropoff, Booking);
            await context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            // Drop only this fixture's pool so its dedicated database can be dropped
            // without interfering with other parallel integration fixtures.
            NpgsqlConnection.ClearPool(new NpgsqlConnection(_connectionString));
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""DROP DATABASE IF EXISTS "{DatabaseName}";""";
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
