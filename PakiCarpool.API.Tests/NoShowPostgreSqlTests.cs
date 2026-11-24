using Microsoft.EntityFrameworkCore;
using Npgsql;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Repository.Dapper;
using PakiCarpool.Repository.EFCore;
using PakiCarpool.Repository.EFCore.Repositories;
using Xunit;

namespace PakiCarpool.API.Tests;

/// <summary>
/// PostgreSQL integration coverage for backlog item 10: the atomic no-show
/// recording persists the NoShowLog, the -10 trust deduction, and the booking
/// cancellation together, and the one-report-per-booking guarantee (ride lock +
/// unique index on NoShowLogs.BookingId) means concurrent reports produce exactly
/// one log and one penalty.
/// </summary>
public sealed class NoShowPostgreSqlTests
{
    public const string ConnectionStringEnvironmentVariable = "PAKICARPOOL_TEST_CONNECTIONSTRING";

    [PostgreSqlFact]
    public async Task ValidNoShow_PersistsLogDeductsPointsAndCancelsBooking()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var repository = fixture.CreateRepository(context);

        var outcome = await repository.RecordNoShowAsync(
            fixture.BuildLog(),
            [BookingStatus.Accepted],
            AppConstants.NoShowDriverCancelReason,
            AppConstants.NoShowPenalty,
            DateTime.UtcNow);

        Assert.Equal(NoShowRecordingOutcome.Recorded, outcome);
        var log = await context.NoShowLogs.AsNoTracking().SingleAsync();
        Assert.Equal(fixture.PassengerId, log.ReporterId);
        Assert.Equal(fixture.DriverUserId, log.OffenderId);
        Assert.Equal(fixture.Ride.Id, log.RideId);
        Assert.Equal(fixture.Booking.Id, log.BookingId);
        var booking = await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == fixture.Booking.Id);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(AppConstants.NoShowDriverCancelReason, booking.CancelReason);
        var passenger = await context.Users.AsNoTracking().SingleAsync(u => u.Id == fixture.PassengerId);
        Assert.Equal(AppConstants.TrustScoreInitial, passenger.TrustScore);
        // The driver is the offender in this direction and takes the -10 penalty.
        var driver = await context.Users.AsNoTracking().SingleAsync(u => u.Id == fixture.DriverUserId);
        Assert.Equal(AppConstants.TrustScoreInitial - AppConstants.NoShowPenalty, driver.TrustScore);
    }

    [PostgreSqlFact]
    public async Task ConcurrentReports_ForSameBooking_ProduceOneLogAndOneDeduction()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        var firstRepository = fixture.CreateRepository(firstContext);
        var secondRepository = fixture.CreateRepository(secondContext);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<NoShowRecordingOutcome> ReportAsync(BookingRepository repository)
        {
            await start.Task;
            return await repository.RecordNoShowAsync(
                fixture.BuildLog(),
                [BookingStatus.Accepted],
                AppConstants.NoShowDriverCancelReason,
                AppConstants.NoShowPenalty,
                DateTime.UtcNow);
        }

        var firstReport = ReportAsync(firstRepository);
        var secondReport = ReportAsync(secondRepository);
        start.SetResult();
        var results = await Task.WhenAll(firstReport, secondReport);

        Assert.Single(results, outcome => outcome == NoShowRecordingOutcome.Recorded);
        Assert.Single(results, outcome => outcome != NoShowRecordingOutcome.Recorded);
        await using var verificationContext = fixture.CreateContext();
        Assert.Equal(1, await verificationContext.NoShowLogs.CountAsync(ns => ns.BookingId == fixture.Booking.Id));
        var reporter = await verificationContext.Users.AsNoTracking()
            .SingleAsync(u => u.Id == fixture.PassengerId);
        Assert.Equal(AppConstants.TrustScoreInitial, reporter.TrustScore);
        // Exactly one -10 deduction lands on the offender.
        var driver = await verificationContext.Users.AsNoTracking()
            .SingleAsync(u => u.Id == fixture.DriverUserId);
        Assert.Equal(AppConstants.TrustScoreInitial - AppConstants.NoShowPenalty, driver.TrustScore);
        var booking = await verificationContext.Bookings.AsNoTracking()
            .SingleAsync(b => b.Id == fixture.Booking.Id);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
    }

    [PostgreSqlFact]
    public async Task DuplicateReport_AfterRecorded_IsRejectedWithoutFurtherPenalty()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        await using var context = fixture.CreateContext();
        var repository = fixture.CreateRepository(context);

        var first = await repository.RecordNoShowAsync(
            fixture.BuildLog(),
            [BookingStatus.Accepted],
            AppConstants.NoShowDriverCancelReason,
            AppConstants.NoShowPenalty,
            DateTime.UtcNow);
        var second = await repository.RecordNoShowAsync(
            fixture.BuildLog(),
            [BookingStatus.Accepted],
            AppConstants.NoShowDriverCancelReason,
            AppConstants.NoShowPenalty,
            DateTime.UtcNow);

        Assert.Equal(NoShowRecordingOutcome.Recorded, first);
        Assert.Equal(NoShowRecordingOutcome.BookingNotEligible, second);
        Assert.Equal(1, await context.NoShowLogs.CountAsync());
        var driver = await context.Users.AsNoTracking().SingleAsync(u => u.Id == fixture.DriverUserId);
        Assert.Equal(AppConstants.TrustScoreInitial - AppConstants.NoShowPenalty, driver.TrustScore);
    }

    [PostgreSqlFact]
    public async Task Report_OnIneligibleBooking_IsRejectedAndPersistsNothing()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync(
            bookingStatus: BookingStatus.Pending);
        await using var context = fixture.CreateContext();
        var repository = fixture.CreateRepository(context);

        var outcome = await repository.RecordNoShowAsync(
            fixture.BuildLog(),
            [BookingStatus.Accepted],
            AppConstants.NoShowDriverCancelReason,
            AppConstants.NoShowPenalty,
            DateTime.UtcNow);

        Assert.Equal(NoShowRecordingOutcome.BookingNotEligible, outcome);
        Assert.Empty(await context.NoShowLogs.ToListAsync());
        var booking = await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == fixture.Booking.Id);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        var driver = await context.Users.AsNoTracking().SingleAsync(u => u.Id == fixture.DriverUserId);
        Assert.Equal(AppConstants.TrustScoreInitial, driver.TrustScore);
    }

    [PostgreSqlFact]
    public async Task Penalty_IsFlooredAtZero()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync(
            offenderTrustScore: AppConstants.NoShowPenalty - 1);
        await using var context = fixture.CreateContext();
        var repository = fixture.CreateRepository(context);

        var outcome = await repository.RecordNoShowAsync(
            fixture.BuildLog(),
            [BookingStatus.Accepted],
            AppConstants.NoShowDriverCancelReason,
            AppConstants.NoShowPenalty,
            DateTime.UtcNow);

        Assert.Equal(NoShowRecordingOutcome.Recorded, outcome);
        var driver = await context.Users.AsNoTracking().SingleAsync(u => u.Id == fixture.DriverUserId);
        Assert.Equal(0, driver.TrustScore);
    }

    private sealed class PostgreSqlFixture : IAsyncDisposable
    {
        private readonly string _adminConnectionString;
        private readonly string _connectionString;
        private readonly DbContextOptions<PakiCarpoolDbContext> _options;
        private PakiCarpoolDbContext? _seedContext;

        /// <summary>
        /// Each fixture gets its own database rather than a schema in the shared test
        /// database: Npgsql's EnsureCreated "has tables" check scans the whole database
        /// (pg_class/pg_namespace across all schemas), so tables from parallel fixtures
        /// in a shared database would make the check skip schema creation elsewhere.
        /// A dedicated database keeps that check — and this suite — deterministic.
        /// </summary>
        private PostgreSqlFixture(string baseConnectionString)
        {
            _adminConnectionString = baseConnectionString;
            DatabaseName = $"paki_no_show_{Guid.NewGuid():N}";
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
        public Ride Ride { get; private set; } = null!;
        public Booking Booking { get; private set; } = null!;
        public Guid DriverUserId { get; private set; }
        public Guid PassengerId { get; private set; }

        public static async Task<PostgreSqlFixture> CreateAsync(
            BookingStatus bookingStatus = BookingStatus.Accepted,
            int offenderTrustScore = AppConstants.TrustScoreInitial)
        {
            var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
                ?? throw new InvalidOperationException("PostgreSQL test connection string is missing.");
            var fixture = new PostgreSqlFixture(connectionString);
            try
            {
                await fixture.CreateDatabaseAsync();
                await fixture.InitializeAsync(bookingStatus, offenderTrustScore);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public PakiCarpoolDbContext CreateContext() => new(_options);

        public BookingRepository CreateRepository(PakiCarpoolDbContext context)
            => new(context, new SeatCapacityQuery(new UnusedConnectionFactory()));

        private async Task CreateDatabaseAsync()
        {
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{DatabaseName}\"";
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Each call builds a fresh log instance, mirroring two independent report
        /// attempts that carry the same booking but distinct row identities.
        /// </summary>
        public NoShowLog BuildLog() => new()
        {
            Id = Guid.NewGuid(),
            ReporterId = PassengerId,
            OffenderId = DriverUserId,
            RideId = Ride.Id,
            BookingId = Booking.Id,
            CreatedAt = DateTime.UtcNow
        };

        private async Task InitializeAsync(BookingStatus bookingStatus, int offenderTrustScore)
        {
            _seedContext = CreateContext();
            await _seedContext.Database.EnsureCreatedAsync();
            var now = DateTime.UtcNow;
            DriverUserId = Guid.NewGuid();
            PassengerId = Guid.NewGuid();
            var driverUser = new User
            {
                Id = DriverUserId,
                FullName = "No-Show Test Driver",
                Email = $"{Guid.NewGuid():N}@example.com",
                Role = UserRole.Driver,
                TrustScore = offenderTrustScore,
                IsActive = true
            };
            var passengerUser = new User
            {
                Id = PassengerId,
                FullName = "No-Show Test Passenger",
                Email = $"{Guid.NewGuid():N}@example.com",
                Role = UserRole.Passenger,
                TrustScore = AppConstants.TrustScoreInitial,
                IsActive = true
            };
            var profile = new DriverProfile
            {
                Id = Guid.NewGuid(),
                UserId = DriverUserId,
                User = driverUser,
                LivenessVerified = true
            };
            var car = new DriverCar
            {
                Id = Guid.NewGuid(),
                DriverId = profile.Id,
                Driver = profile,
                Make = "Test",
                Model = "Car",
                Year = 2026,
                LicensePlate = "TEST-PLATE",
                AvailableSeats = 2,
                Status = CarStatus.Approved
            };
            Ride = new Ride
            {
                Id = Guid.NewGuid(),
                DriverId = profile.Id,
                Driver = profile,
                CarId = car.Id,
                Car = car,
                Fare = 100,
                MaxSeats = 2,
                Status = RideStatus.Active,
                CreatedAt = now.AddMinutes(-20),
                CancelGraceExpiresAt = now.AddMinutes(-15)
            };
            var pickup = new RideStop
            {
                Id = Guid.NewGuid(),
                RideId = Ride.Id,
                Ride = Ride,
                Name = "Pickup",
                SequenceOrder = 1
            };
            var dropoff = new RideStop
            {
                Id = Guid.NewGuid(),
                RideId = Ride.Id,
                Ride = Ride,
                Name = "Dropoff",
                SequenceOrder = 2
            };
            Ride.Stops = [pickup, dropoff];
            Booking = new Booking
            {
                Id = Guid.NewGuid(),
                RideId = Ride.Id,
                Ride = Ride,
                PassengerId = PassengerId,
                Passenger = passengerUser,
                PickupStopId = pickup.Id,
                PickupStop = pickup,
                DropoffStopId = dropoff.Id,
                DropoffStop = dropoff,
                Status = bookingStatus,
                RequestedAt = now.AddMinutes(-10),
                AcceptedAt = bookingStatus == BookingStatus.Accepted ? now.AddMinutes(-5) : null
            };

            _seedContext.AddRange(driverUser, passengerUser, profile, car, Ride, pickup, dropoff, Booking);
            await _seedContext.SaveChangesAsync();
            _seedContext.ChangeTracker.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            if (_seedContext is not null)
                await _seedContext.DisposeAsync();

            // Drop only this fixture's pool so the database can be dropped while
            // parallel fixtures keep using their own pools.
            NpgsqlConnection.ClearPool(new NpgsqlConnection(_connectionString));
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS \"{DatabaseName}\"";
            await command.ExecuteNonQueryAsync();
        }

        private sealed class UnusedConnectionFactory : IDapperConnectionFactory
        {
            public System.Data.IDbConnection CreateConnection()
                => throw new InvalidOperationException(
                    "No-show recording must not need the Dapper seat capacity query.");
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
