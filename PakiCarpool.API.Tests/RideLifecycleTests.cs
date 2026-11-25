using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.DTOs.Driver;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Repository.EFCore;
using PakiCarpool.Security.Helpers;
using PakiCarpool.Services.Implementations;
using Xunit;

namespace PakiCarpool.API.Tests;

public sealed class RideLifecycleTests
{
    [Fact]
    public async Task PostedRide_ValidFirstArrivalAndPassengerDropoff_ReachesCompleted()
    {
        var fixture = new ServiceFixture();

        var beginPickup = await fixture.Service.BeginStopTravelAsync(fixture.DriverId, fixture.Pickup.Id);
        Assert.True(beginPickup.IsSuccess, beginPickup.Message);
        Assert.Equal(RideStopStatus.Active, fixture.Pickup.Status);
        Assert.Equal(RideStatus.Posted, fixture.Ride.Status);

        var postedLocation = await fixture.UpdateLocationAtAsync(fixture.Pickup);
        Assert.True(postedLocation.IsSuccess, postedLocation.Message);
        Assert.NotNull(fixture.Location);

        var firstArrival = await fixture.Service.MarkStopArrivedAsync(fixture.DriverId, fixture.Pickup.Id);
        Assert.True(firstArrival.IsSuccess, firstArrival.Message);
        Assert.Equal(RideStatus.Active, fixture.Ride.Status);
        Assert.NotNull(fixture.Ride.StartedAt);
        Assert.Equal(RideStopStatus.Completed, fixture.Pickup.Status);
        Assert.Equal(BookingStatus.Active, fixture.Booking.Status);

        var beginDropoff = await fixture.Service.BeginStopTravelAsync(fixture.DriverId, fixture.Dropoff.Id);
        Assert.True(beginDropoff.IsSuccess, beginDropoff.Message);
        var activeLocation = await fixture.UpdateLocationAtAsync(fixture.Dropoff);
        Assert.True(activeLocation.IsSuccess, activeLocation.Message);
        var finalArrival = await fixture.Service.MarkStopArrivedAsync(fixture.DriverId, fixture.Dropoff.Id);
        Assert.True(finalArrival.IsSuccess, finalArrival.Message);
        Assert.Equal(RideStopStatus.Completed, fixture.Dropoff.Status);

        var endRide = await fixture.Service.EndRideForPassengerAsync(fixture.DriverId, fixture.Booking.Id);

        Assert.True(endRide.IsSuccess, endRide.Message);
        Assert.Equal(BookingStatus.Completed, fixture.Booking.Status);
        Assert.Equal(RideStatus.Completed, fixture.Ride.Status);
        Assert.NotNull(fixture.Ride.CompletedAt);
        fixture.RideRepository.Verify(
            repository => repository.UpsertDriverLocationAsync(It.IsAny<DriverLocation>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task MarkStopArrived_WrongDriver_IsRejected()
    {
        var fixture = new ServiceFixture();
        fixture.PreparePickupArrival();

        var result = await fixture.Service.MarkStopArrivedAsync(fixture.OtherDriverId, fixture.Pickup.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(RideStatus.Posted, fixture.Ride.Status);
        Assert.Equal(RideStopStatus.Active, fixture.Pickup.Status);
        fixture.RideRepository.Verify(
            repository => repository.UpdateStopAsync(It.IsAny<RideStop>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateLocation_WrongDriver_IsRejected()
    {
        var fixture = new ServiceFixture();

        var result = await fixture.Service.UpdateLocationAsync(fixture.OtherDriverId, new DriverLocationDto
        {
            RideId = fixture.Ride.Id,
            Lat = fixture.Pickup.Latitude,
            Lng = fixture.Pickup.Longitude
        });

        Assert.False(result.IsSuccess);
        fixture.RideRepository.Verify(
            repository => repository.UpsertDriverLocationAsync(It.IsAny<DriverLocation>()),
            Times.Never);
    }

    [Fact]
    public async Task MarkStopArrived_StaleGps_IsRejected()
    {
        var fixture = new ServiceFixture();
        fixture.PreparePickupArrival(recordedAt: DateTime.UtcNow.AddSeconds(-31));

        var result = await fixture.Service.MarkStopArrivedAsync(fixture.DriverId, fixture.Pickup.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("A recent driver location is required to confirm arrival.", result.Message);
        Assert.Equal(RideStatus.Posted, fixture.Ride.Status);
        Assert.Equal(RideStopStatus.Active, fixture.Pickup.Status);
        Assert.Null(fixture.Pickup.ArrivedAt);
    }

    [Fact]
    public async Task MarkStopArrived_OutOfRadius_IsRejected()
    {
        var fixture = new ServiceFixture();
        fixture.PreparePickupArrival(latitude: fixture.Pickup.Latitude + 0.01);

        var result = await fixture.Service.MarkStopArrivedAsync(fixture.DriverId, fixture.Pickup.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("Driver must be within 150 meters of the stop to confirm arrival.", result.Message);
        Assert.Equal(RideStatus.Posted, fixture.Ride.Status);
        Assert.Equal(RideStopStatus.Active, fixture.Pickup.Status);
        Assert.Null(fixture.Pickup.ArrivedAt);
    }

    [Theory]
    [InlineData(RideStatus.Cancelled)]
    [InlineData(RideStatus.Completed)]
    public async Task MarkStopArrived_TerminalRide_IsRejected(RideStatus status)
    {
        var fixture = new ServiceFixture();
        fixture.Ride.Status = status;
        fixture.PreparePickupArrival();

        var result = await fixture.Service.MarkStopArrivedAsync(fixture.DriverId, fixture.Pickup.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("Ride is not active.", result.Message);
        Assert.Equal(RideStopStatus.Active, fixture.Pickup.Status);
        fixture.RideRepository.Verify(
            repository => repository.UpdateStopAsync(It.IsAny<RideStop>()),
            Times.Never);
    }

    [Theory]
    [InlineData(RideStatus.Cancelled)]
    [InlineData(RideStatus.Completed)]
    public async Task UpdateLocation_TerminalRide_IsRejected(RideStatus status)
    {
        var fixture = new ServiceFixture();
        fixture.Ride.Status = status;

        var result = await fixture.UpdateLocationAtAsync(fixture.Pickup);

        Assert.False(result.IsSuccess);
        Assert.Equal("Location can only be updated for a posted or active ride.", result.Message);
        fixture.RideRepository.Verify(
            repository => repository.UpsertDriverLocationAsync(It.IsAny<DriverLocation>()),
            Times.Never);
    }

    private sealed class ServiceFixture
    {
        public ServiceFixture()
        {
            Profile = new DriverProfile { Id = Guid.NewGuid(), UserId = DriverId };
            OtherProfile = new DriverProfile { Id = Guid.NewGuid(), UserId = OtherDriverId };
            Ride = new Ride
            {
                Id = Guid.NewGuid(),
                DriverId = Profile.Id,
                Driver = Profile,
                Status = RideStatus.Posted,
                Fare = 500
            };
            Pickup = new RideStop
            {
                Id = Guid.NewGuid(),
                RideId = Ride.Id,
                Name = "Pickup",
                Latitude = 24.8607,
                Longitude = 67.0011,
                SequenceOrder = 1
            };
            Dropoff = new RideStop
            {
                Id = Guid.NewGuid(),
                RideId = Ride.Id,
                Name = "Dropoff",
                Latitude = 24.8707,
                Longitude = 67.0111,
                SequenceOrder = 2
            };
            Ride.Stops = new List<RideStop> { Pickup, Dropoff };
            Booking = new Booking
            {
                Id = Guid.NewGuid(),
                RideId = Ride.Id,
                Ride = Ride,
                PassengerId = Guid.NewGuid(),
                PickupStopId = Pickup.Id,
                DropoffStopId = Dropoff.Id,
                Status = BookingStatus.Accepted
            };
            Ride.Bookings = new List<Booking> { Booking };

            UserRepository
                .Setup(repository => repository.GetDriverProfileByUserIdAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid userId, CancellationToken _) =>
                    userId == DriverId ? Profile : OtherProfile);
            RideRepository
                .Setup(repository => repository.GetByIdAsync(
                    Ride.Id,
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Ride);
            RideRepository
                .Setup(repository => repository.GetStopByIdAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid stopId, CancellationToken _) =>
                    Ride.Stops.SingleOrDefault(stop => stop.Id == stopId));
            RideRepository
                .Setup(repository => repository.UpsertDriverLocationAsync(It.IsAny<DriverLocation>()))
                .Callback<DriverLocation>(location => Location = location)
                .Returns(Task.CompletedTask);
            RideRepository
                .Setup(repository => repository.GetDriverLocationAsync(Profile.Id, Ride.Id))
                .ReturnsAsync(() => Location);
            BookingRepository
                .Setup(repository => repository.GetByIdAsync(
                    Booking.Id,
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Booking);
            BookingRepository
                .Setup(repository => repository.AreAllBookingsCompletedOrCancelledAsync(Ride.Id))
                .ReturnsAsync(() => Booking.Status is BookingStatus.Completed or BookingStatus.Cancelled);

            Context.Setup(context => context.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);
            HubClients.Setup(clients => clients.Group(It.IsAny<string>()))
                .Returns(ClientProxy.Object);
            HubContext.SetupGet(context => context.Clients).Returns(HubClients.Object);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AES_KEY_V1"] = Convert.ToBase64String(new byte[32])
                })
                .Build();
            Service = new DriverService(
                UserRepository.Object,
                Context.Object,
                RideRepository.Object,
                BookingRepository.Object,
                Notifications.Object,
                Flags.Object,
                Trust.Object,
                Email.Object,
                new AesEncryptionHelper(configuration),
                new Mock<IStorageService>().Object,
                HubContext.Object);
        }

        public Guid DriverId { get; } = Guid.NewGuid();
        public Guid OtherDriverId { get; } = Guid.NewGuid();
        public DriverProfile Profile { get; }
        public DriverProfile OtherProfile { get; }
        public Ride Ride { get; }
        public RideStop Pickup { get; }
        public RideStop Dropoff { get; }
        public Booking Booking { get; }
        public DriverLocation? Location { get; private set; }
        public DriverService Service { get; }
        public Mock<IUserRepository> UserRepository { get; } = new();
        public Mock<IRideRepository> RideRepository { get; } = new();
        public Mock<IBookingRepository> BookingRepository { get; } = new();
        public Mock<INotificationService> Notifications { get; } = new();
        public Mock<IFlagService> Flags { get; } = new();
        public Mock<ITrustService> Trust { get; } = new();
        public Mock<IEmailService> Email { get; } = new();
        public Mock<PakiCarpoolDbContext> Context { get; } = new(
            new DbContextOptionsBuilder<PakiCarpoolDbContext>()
                .UseNpgsql("Host=localhost;Database=ride_lifecycle_tests")
                .Options)
        {
            CallBase = true
        };
        public Mock<IHubContext<Hub>> HubContext { get; } = new();
        private Mock<IHubClients> HubClients { get; } = new();
        private Mock<IClientProxy> ClientProxy { get; } = new();

        public Task<ApiResponse<object>> UpdateLocationAtAsync(RideStop stop)
            => Service.UpdateLocationAsync(DriverId, new DriverLocationDto
            {
                RideId = Ride.Id,
                Lat = stop.Latitude,
                Lng = stop.Longitude
            });

        public void PreparePickupArrival(
            DateTime? recordedAt = null,
            double? latitude = null,
            double? longitude = null)
        {
            Pickup.Status = RideStopStatus.Active;
            Location = new DriverLocation
            {
                DriverId = Profile.Id,
                RideId = Ride.Id,
                Latitude = latitude ?? Pickup.Latitude,
                Longitude = longitude ?? Pickup.Longitude,
                RecordedAt = recordedAt ?? DateTime.UtcNow
            };
        }
    }
}
