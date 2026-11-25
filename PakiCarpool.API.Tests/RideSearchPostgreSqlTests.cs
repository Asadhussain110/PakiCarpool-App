using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using PakiCarpool.Domain.DTOs.Passenger;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Repository.Dapper;
using PakiCarpool.Repository.EFCore;
using PakiCarpool.Repository.EFCore.Repositories;
using PakiCarpool.Services.Implementations;
using PakiCarpool.Services.Options;
using Xunit;

namespace PakiCarpool.API.Tests;

public sealed class RideSearchPostgreSqlTests
{
    private const string ConnectionStringEnvironmentVariable = "PAKICARPOOL_TEST_CONNECTIONSTRING";
    private const double StartLatitude = 24.9000;
    private const double StartLongitude = 67.1000;
    private const double EndLatitude = 24.9000;
    private const double EndLongitude = 67.1200;

    [PostgreSqlFact]
    public async Task Search_ReversedNearestStops_FindsAnotherValidOrderedPair()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        var ride = await fixture.AddRideAsync(
            RideStatus.Posted,
            maxSeats: 2,
            (EndLatitude, EndLongitude),
            (24.9000, 67.1100),
            (24.9000, 67.1100),
            (StartLatitude, StartLongitude),
            (MetersNorth(EndLatitude, 200), EndLongitude));

        var result = Assert.Single(await fixture.SearchAsync());

        Assert.Equal(ride.Id, result.RideId);
        Assert.Equal(Stop(ride, 3).Id, result.StartStop.StopId);
        Assert.Equal(Stop(ride, 4).Id, result.EndStop.StopId);
        Assert.NotEqual(Stop(ride, 0).Id, result.EndStop.StopId);
        Assert.Equal(2, result.MatchRank);
    }

    [PostgreSqlFact]
    public async Task Search_NonOverlappingSubSegments_DoNotConsumeEachOthersSeats()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        var ride = await fixture.AddRideAsync(
            RideStatus.Posted,
            maxSeats: 1,
            (StartLatitude, StartLongitude),
            (StartLatitude, StartLongitude),
            (EndLatitude, EndLongitude),
            (EndLatitude, EndLongitude));
        await fixture.AddBookingAsync(ride, Stop(ride, 0), Stop(ride, 1), BookingStatus.Pending);
        await fixture.AddBookingAsync(ride, Stop(ride, 2), Stop(ride, 3), BookingStatus.Accepted);
        await fixture.AddBookingAsync(ride, Stop(ride, 1), Stop(ride, 2), BookingStatus.Cancelled);

        var result = Assert.Single(await fixture.SearchAsync());

        Assert.Equal(Stop(ride, 1).Id, result.StartStop.StopId);
        Assert.Equal(Stop(ride, 2).Id, result.EndStop.StopId);
        Assert.Equal(1, result.AvailableSeats);
    }

    [PostgreSqlFact]
    public async Task Search_FullSegmentInsideRange_ExcludesRideForEveryOccupyingStatus()
    {
        foreach (var status in new[] { BookingStatus.Pending, BookingStatus.Accepted, BookingStatus.Active })
        {
            await using var fixture = await PostgreSqlFixture.CreateAsync();
            var ride = await fixture.AddRideAsync(
                RideStatus.Posted,
                maxSeats: 1,
                (StartLatitude, StartLongitude),
                (24.9000, 67.1100),
                (EndLatitude, EndLongitude));
            await fixture.AddBookingAsync(ride, Stop(ride, 1), Stop(ride, 2), status);

            var results = await fixture.SearchAsync();

            Assert.True(results.Count == 0, $"A {status} booking must fill its route segment.");
        }
    }

    [PostgreSqlFact]
    public async Task Search_MultipleValidPairs_ChoosesBestMatchRankThenCombinedDistance()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        var ride = await fixture.AddRideAsync(
            RideStatus.Posted,
            maxSeats: 2,
            (StartLatitude, StartLongitude),
            (24.9000, 67.1100),
            (MetersNorth(EndLatitude, 40), EndLongitude),
            (MetersNorth(EndLatitude, 30), EndLongitude),
            (MetersNorth(EndLatitude, 60), EndLongitude));

        var result = Assert.Single(await fixture.SearchAsync());

        Assert.Equal(Stop(ride, 0).Id, result.StartStop.StopId);
        Assert.Equal(Stop(ride, 3).Id, result.EndStop.StopId);
        Assert.Equal(1, result.MatchRank);
        Assert.Null(result.StartStop.DistanceMeters);
        Assert.Null(result.EndStop.DistanceMeters);
    }

    [PostgreSqlFact]
    public async Task Search_CancelledAndCompletedRides_AreExcluded()
    {
        await using var fixture = await PostgreSqlFixture.CreateAsync();
        var posted = await fixture.AddDefaultRideAsync(RideStatus.Posted);
        var active = await fixture.AddDefaultRideAsync(RideStatus.Active);
        await fixture.AddDefaultRideAsync(RideStatus.Cancelled);
        await fixture.AddDefaultRideAsync(RideStatus.Completed);

        var results = await fixture.SearchAsync();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, result => result.RideId == posted.Id);
        Assert.Contains(results, result => result.RideId == active.Id);
    }

    private static double MetersNorth(double latitude, double meters)
        => latitude + meters / 111000d;

    private static RideStop Stop(Ride ride, int index)
        => ride.Stops.OrderBy(stop => stop.SequenceOrder).ElementAt(index);

    private sealed class PostgreSqlFixture : IAsyncDisposable
    {
        private readonly string _baseConnectionString;
        private readonly string _schemaConnectionString;
        private readonly DbContextOptions<PakiCarpoolDbContext> _options;
        private PakiCarpoolDbContext? _seedContext;

        private PostgreSqlFixture(string baseConnectionString)
        {
            _baseConnectionString = baseConnectionString;
            Schema = $"ride_search_{Guid.NewGuid():N}";
            var builder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                SearchPath = Schema
            };
            _schemaConnectionString = builder.ConnectionString;
            _options = new DbContextOptionsBuilder<PakiCarpoolDbContext>()
                .UseNpgsql(_schemaConnectionString)
                .Options;
        }

        public string Schema { get; }

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

        public Task<Ride> AddDefaultRideAsync(RideStatus status)
            => AddRideAsync(
                status,
                maxSeats: 2,
                (StartLatitude, StartLongitude),
                (EndLatitude, EndLongitude));

        public async Task<Ride> AddRideAsync(
            RideStatus status,
            int maxSeats,
            params (double Latitude, double Longitude)[] stopCoordinates)
        {
            var now = DateTime.UtcNow;
            var driverUser = new User
            {
                Id = Guid.NewGuid(),
                FullName = "Ride Search Test Driver",
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
                LicensePlate = $"TEST-{Guid.NewGuid():N}",
                AvailableSeats = maxSeats,
                Status = CarStatus.Approved
            };
            var ride = new Ride
            {
                Id = Guid.NewGuid(),
                DriverId = profile.Id,
                Driver = profile,
                CarId = car.Id,
                Car = car,
                Fare = 100,
                MaxSeats = maxSeats,
                Status = status,
                CreatedAt = now.AddMinutes(-5),
                CancelGraceExpiresAt = now.AddMinutes(5)
            };
            ride.Stops = stopCoordinates.Select((coordinate, index) => new RideStop
            {
                Id = Guid.NewGuid(),
                RideId = ride.Id,
                Ride = ride,
                Name = $"Stop {index + 1}",
                Latitude = coordinate.Latitude,
                Longitude = coordinate.Longitude,
                SequenceOrder = index + 1,
                Status = RideStopStatus.Upcoming
            }).ToList();

            _seedContext!.AddRange(driverUser, profile, car, ride);
            await _seedContext.SaveChangesAsync();
            _seedContext.ChangeTracker.Clear();
            return ride;
        }

        public async Task<Booking> AddBookingAsync(
            Ride ride,
            RideStop pickup,
            RideStop dropoff,
            BookingStatus status)
        {
            var now = DateTime.UtcNow;
            var passenger = new User
            {
                Id = Guid.NewGuid(),
                FullName = "Ride Search Test Passenger",
                Email = $"{Guid.NewGuid():N}@example.com",
                Role = UserRole.Passenger
            };
            var booking = new Booking
            {
                Id = Guid.NewGuid(),
                RideId = ride.Id,
                PassengerId = passenger.Id,
                Passenger = passenger,
                PickupStopId = pickup.Id,
                DropoffStopId = dropoff.Id,
                Status = status,
                RequestedAt = now.AddMinutes(-1),
                AcceptedAt = status is BookingStatus.Accepted or BookingStatus.Active ? now : null
            };

            _seedContext!.AddRange(passenger, booking);
            await _seedContext.SaveChangesAsync();
            _seedContext.ChangeTracker.Clear();
            return booking;
        }

        public async Task<List<RideSearchResultDto>> SearchAsync()
        {
            await using var context = new PakiCarpoolDbContext(_options);
            var repository = new RideRepository(
                context,
                new RideSearchQuery(new SchemaConnectionFactory(_schemaConnectionString)));
            var service = new RideSearchService(
                repository,
                Options.Create(new RideSearchOptions
                {
                    PerfectMatchToleranceMeters = 50,
                    DefaultRadiusMeters = 500,
                    MinRadiusMeters = 100,
                    MaxRadiusMeters = 5000
                }));
            var results = await service.SearchRidesAsync(new RideSearchRequestDto
            {
                StartLatitude = StartLatitude,
                StartLongitude = StartLongitude,
                EndLatitude = EndLatitude,
                EndLongitude = EndLongitude,
                RadiusMeters = 500
            });
            return results.ToList();
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

            _seedContext = new PakiCarpoolDbContext(_options);
            await _seedContext.Database.EnsureCreatedAsync();
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

        private sealed class SchemaConnectionFactory : IDapperConnectionFactory
        {
            private readonly string _connectionString;

            public SchemaConnectionFactory(string connectionString)
            {
                _connectionString = connectionString;
            }

            public IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);
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
