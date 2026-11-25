using System.Data;
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

public sealed class BookingAcceptancePostgreSqlTests
{
    public const string ConnectionStringEnvironmentVariable = "PAKICARPOOL_TEST_CONNECTIONSTRING";

    [PostgreSqlFact]
    public async Task ConcurrentAcceptance_WithPendingCompetitor_NeitherOverbooks()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync(maxSeats: 1, bookingCount: 2);
        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        var firstRepository = fixture.CreateRepository(firstContext);
        var secondRepository = fixture.CreateRepository(secondContext);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<(BookingAcceptanceOutcome Outcome, Booking? Booking)> AcceptAsync(
            BookingRepository repository,
            Guid bookingId)
        {
            await start.Task;
            return await repository.AcceptPendingAsync(bookingId, DateTime.UtcNow);
        }

        var firstAttempt = AcceptAsync(firstRepository, fixture.Bookings[0].Id);
        var secondAttempt = AcceptAsync(secondRepository, fixture.Bookings[1].Id);
        start.SetResult();
        var results = await Task.WhenAll(firstAttempt, secondAttempt);

        Assert.All(results, result => Assert.Equal(BookingAcceptanceOutcome.SegmentFull, result.Outcome));
        await using var verificationContext = fixture.CreateContext();
        Assert.Equal(
            0,
            await verificationContext.Bookings.CountAsync(b =>
                b.RideId == fixture.Ride.Id && b.Status == BookingStatus.Accepted));
        Assert.Equal(
            2,
            await verificationContext.Bookings.CountAsync(b =>
                b.RideId == fixture.Ride.Id && b.Status == BookingStatus.Pending));
    }

    [PostgreSqlFact]
    public async Task Acceptance_WithAvailableSegment_SucceedsWithoutCountingItself()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync(maxSeats: 1);
        await using var context = fixture.CreateContext();
        var repository = fixture.CreateRepository(context);

        var result = await repository.AcceptPendingAsync(fixture.Bookings[0].Id, DateTime.UtcNow);

        Assert.Equal(BookingAcceptanceOutcome.Accepted, result.Outcome);
        var persisted = await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == fixture.Bookings[0].Id);
        Assert.Equal(BookingStatus.Accepted, persisted.Status);
        Assert.NotNull(persisted.AcceptedAt);
    }

    [PostgreSqlFact]
    public async Task Acceptance_OnCancelledRide_IsRejected()
    {
        await AssertTerminalRideRejectedAsync(
            RideStatus.Cancelled,
            BookingAcceptanceOutcome.RideCancelled);
    }

    [PostgreSqlFact]
    public async Task Acceptance_OnCompletedRide_IsRejected()
    {
        await AssertTerminalRideRejectedAsync(
            RideStatus.Completed,
            BookingAcceptanceOutcome.RideCompleted);
    }

    [PostgreSqlFact]
    public async Task Acceptance_AfterTimeout_CancelsOnceAndCannotBeAcceptedLater()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync(
            requestedAt: DateTime.UtcNow.AddMinutes(-3));
        await using var context = fixture.CreateContext();
        var repository = fixture.CreateRepository(context);
        var now = DateTime.UtcNow;

        var expiredAcceptance = await repository.AcceptPendingAsync(fixture.Bookings[0].Id, now);
        var duplicateTimeout = await repository.CancelTimedOutAsync(
            fixture.Bookings[0].Id,
            now.AddMinutes(-AppConstants.BookingTimeoutMinutes));
        var laterAcceptance = await repository.AcceptPendingAsync(fixture.Bookings[0].Id, now);

        Assert.Equal(BookingAcceptanceOutcome.TimedOut, expiredAcceptance.Outcome);
        Assert.Null(duplicateTimeout);
        Assert.Equal(BookingAcceptanceOutcome.Cancelled, laterAcceptance.Outcome);
        var persisted = await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == fixture.Bookings[0].Id);
        Assert.Equal(BookingStatus.Cancelled, persisted.Status);
        Assert.Equal(AppConstants.BookingTimeoutCancelReason, persisted.CancelReason);
        Assert.Null(persisted.AcceptedAt);
    }

    [PostgreSqlFact]
    public async Task Acceptance_OfAlreadyAcceptedBooking_IsRejected()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync(
            initialBookingStatus: BookingStatus.Accepted);
        await using var context = fixture.CreateContext();
        var repository = fixture.CreateRepository(context);

        var result = await repository.AcceptPendingAsync(fixture.Bookings[0].Id, DateTime.UtcNow);

        Assert.Equal(BookingAcceptanceOutcome.AlreadyAccepted, result.Outcome);
        var persisted = await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == fixture.Bookings[0].Id);
        Assert.Equal(BookingStatus.Accepted, persisted.Status);
    }

    [PostgreSqlFact]
    public async Task AcceptanceAndTimeout_Racing_ProduceOnlyOneCancellation()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync(
            requestedAt: DateTime.UtcNow.AddMinutes(-3));
        await using var acceptanceContext = fixture.CreateContext();
        await using var timeoutContext = fixture.CreateContext();
        var acceptanceRepository = fixture.CreateRepository(acceptanceContext);
        var timeoutRepository = fixture.CreateRepository(timeoutContext);
        var now = DateTime.UtcNow;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var acceptanceTask = AcceptAfterStartAsync(
            acceptanceRepository,
            fixture.Bookings[0].Id,
            now,
            start.Task);
        var timeoutTask = TimeoutAfterStartAsync(
            timeoutRepository,
            fixture.Bookings[0].Id,
            now.AddMinutes(-AppConstants.BookingTimeoutMinutes),
            start.Task);
        start.SetResult();
        await Task.WhenAll(acceptanceTask, timeoutTask);
        var acceptance = await acceptanceTask;
        var timedOut = await timeoutTask;

        var cancelledByAcceptance = acceptance.Outcome == BookingAcceptanceOutcome.TimedOut;
        var cancelledByTimeout = timedOut is not null;
        Assert.NotEqual(cancelledByAcceptance, cancelledByTimeout);
        Assert.True(
            cancelledByAcceptance || acceptance.Outcome == BookingAcceptanceOutcome.Cancelled,
            $"Unexpected acceptance outcome: {acceptance.Outcome}");
        await using var verificationContext = fixture.CreateContext();
        var persisted = await verificationContext.Bookings.SingleAsync(b => b.Id == fixture.Bookings[0].Id);
        Assert.Equal(BookingStatus.Cancelled, persisted.Status);
        Assert.Null(persisted.AcceptedAt);
    }

    private static async Task<(BookingAcceptanceOutcome Outcome, Booking? Booking)> AcceptAfterStartAsync(
        BookingRepository repository,
        Guid bookingId,
        DateTime now,
        Task start)
    {
        await start;
        return await repository.AcceptPendingAsync(bookingId, now);
    }

    private static async Task<Booking?> TimeoutAfterStartAsync(
        BookingRepository repository,
        Guid bookingId,
        DateTime cutoff,
        Task start)
    {
        await start;
        return await repository.CancelTimedOutAsync(bookingId, cutoff);
    }

    private static async Task AssertTerminalRideRejectedAsync(
        RideStatus rideStatus,
        BookingAcceptanceOutcome expectedOutcome)
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync(rideStatus: rideStatus);
        await using var context = fixture.CreateContext();
        var repository = fixture.CreateRepository(context);

        var result = await repository.AcceptPendingAsync(fixture.Bookings[0].Id, DateTime.UtcNow);

        Assert.Equal(expectedOutcome, result.Outcome);
        var persisted = await context.Bookings.AsNoTracking().SingleAsync(b => b.Id == fixture.Bookings[0].Id);
        Assert.Equal(BookingStatus.Pending, persisted.Status);
        Assert.Null(persisted.AcceptedAt);
    }

    private sealed class PostgreSqlFixture : IAsyncDisposable
    {
        private readonly string _baseConnectionString;
        private readonly DbContextOptions<PakiCarpoolDbContext> _options;
        private PakiCarpoolDbContext? _seedContext;

        private PostgreSqlFixture(string baseConnectionString)
        {
            _baseConnectionString = baseConnectionString;
            Schema = $"booking_acceptance_{Guid.NewGuid():N}";
            var builder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                SearchPath = Schema
            };
            _options = new DbContextOptionsBuilder<PakiCarpoolDbContext>()
                .UseNpgsql(builder.ConnectionString)
                .Options;
        }

        public string Schema { get; }
        public Ride Ride { get; private set; } = null!;
        public List<Booking> Bookings { get; private set; } = [];

        public static async Task<PostgreSqlFixture> CreateAsync(
            int maxSeats = 2,
            int bookingCount = 1,
            RideStatus rideStatus = RideStatus.Posted,
            BookingStatus initialBookingStatus = BookingStatus.Pending,
            DateTime? requestedAt = null)
        {
            var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
                ?? throw new InvalidOperationException("PostgreSQL test connection string is missing.");
            var fixture = new PostgreSqlFixture(connectionString);
            try
            {
                await fixture.InitializeAsync(
                    maxSeats,
                    bookingCount,
                    rideStatus,
                    initialBookingStatus,
                    requestedAt);
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

        private async Task InitializeAsync(
            int maxSeats,
            int bookingCount,
            RideStatus rideStatus,
            BookingStatus initialBookingStatus,
            DateTime? requestedAt)
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
            var now = DateTime.UtcNow;
            var driverUser = new User
            {
                Id = Guid.NewGuid(),
                FullName = "Booking Test Driver",
                Email = $"{Guid.NewGuid():N}@example.com",
                Role = UserRole.Driver
            };
            var profile = new DriverProfile
            {
                Id = Guid.NewGuid(),
                UserId = driverUser.Id,
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
                AvailableSeats = maxSeats,
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
                MaxSeats = maxSeats,
                Status = rideStatus,
                CreatedAt = now.AddMinutes(-5),
                CancelGraceExpiresAt = now.AddMinutes(5)
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
            Bookings = [];
            for (var index = 0; index < bookingCount; index++)
            {
                var passenger = new User
                {
                    Id = Guid.NewGuid(),
                    FullName = $"Booking Test Passenger {index}",
                    Email = $"{Guid.NewGuid():N}@example.com",
                    Role = UserRole.Passenger
                };
                Bookings.Add(new Booking
                {
                    Id = Guid.NewGuid(),
                    RideId = Ride.Id,
                    Ride = Ride,
                    PassengerId = passenger.Id,
                    Passenger = passenger,
                    PickupStopId = pickup.Id,
                    PickupStop = pickup,
                    DropoffStopId = dropoff.Id,
                    DropoffStop = dropoff,
                    Status = initialBookingStatus,
                    RequestedAt = requestedAt ?? now.AddMinutes(-1),
                    AcceptedAt = initialBookingStatus == BookingStatus.Accepted ? now : null
                });
            }

            _seedContext.AddRange(driverUser, profile, car, Ride, pickup, dropoff);
            _seedContext.AddRange(Bookings.Select(booking => booking.Passenger));
            _seedContext.AddRange(Bookings);
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

        private sealed class UnusedConnectionFactory : IDapperConnectionFactory
        {
            public IDbConnection CreateConnection()
                => throw new InvalidOperationException(
                    "Atomic acceptance must use the EF transaction connection for its Dapper capacity query.");
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
