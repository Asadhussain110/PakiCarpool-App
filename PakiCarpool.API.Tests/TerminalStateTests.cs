using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using PakiCarpool.Domain.DTOs.Driver;
using PakiCarpool.Domain.DTOs.Passenger;
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

public sealed class TerminalStateTests
{
    [Fact]
    public async Task CancelRide_WithPendingAndConfirmedBookings_CancelsAndNotifiesEachPassenger()
    {
        using var fixture = new ServiceFixture();
        var accepted = fixture.Booking;
        var pending = fixture.AddBooking(BookingStatus.Pending);
        var active = fixture.AddBooking(BookingStatus.Active);
        var rejected = fixture.AddBooking(BookingStatus.Rejected);
        var alreadyCancelled = fixture.AddBooking(BookingStatus.Cancelled);

        var result = await fixture.DriverService.CancelRideAsync(
            fixture.DriverId,
            fixture.Ride.Id,
            new CancelRideDto { Reason = "The driver can no longer travel." });

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(RideStatus.Cancelled, fixture.Ride.Status);
        foreach (var booking in new[] { pending, accepted, active })
        {
            Assert.Equal(BookingStatus.Cancelled, booking.Status);
            Assert.Equal("Ride cancelled by driver", booking.CancelReason);
            fixture.Notifications.Verify(
                service => service.RemovePassengerFromRideGroupsAsync(
                    fixture.Ride.Id,
                    booking.PassengerId,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            fixture.Notifications.Verify(
                service => service.SendInAppNotificationAsync(
                    booking.PassengerId,
                    NotificationType.RideCancelledByDriver,
                    "Ride Cancelled",
                    "Your upcoming ride has been cancelled by the driver.",
                    It.IsAny<CancellationToken>()),
                Times.Once);
            fixture.Email.Verify(
                service => service.SendRideCancelledEmailAsync(
                    booking.Passenger.Email,
                    booking.Passenger.FullName,
                    It.IsAny<string>()),
                Times.Once);
        }

        Assert.Equal(BookingStatus.Rejected, rejected.Status);
        Assert.Equal(BookingStatus.Cancelled, alreadyCancelled.Status);
        fixture.Rides.Verify(
            repository => repository.GetByIdAsync(
                fixture.Ride.Id,
                true,
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Notifications.Verify(
            service => service.SendRideCancelledByDriverAsync(
                fixture.Ride.Id,
                "The driver can no longer travel."),
            Times.Once);
        fixture.Notifications.Verify(
            service => service.RemovePassengerFromRideGroupsAsync(
                It.IsAny<Guid>(),
                rejected.PassengerId,
                It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.Notifications.Verify(
            service => service.RemovePassengerFromRideGroupsAsync(
                It.IsAny<Guid>(),
                alreadyCancelled.PassengerId,
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(BookingStatus.Rejected)]
    [InlineData(BookingStatus.Cancelled)]
    public async Task EndRideForPassenger_WithNonOccupyingBooking_CompletesRide(
        BookingStatus otherBookingStatus)
    {
        using var fixture = new ServiceFixture();
        var otherBooking = fixture.AddBooking(otherBookingStatus);
        fixture.Dropoff.Status = RideStopStatus.Completed;
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.DriverService.EndRideForPassengerAsync(
            fixture.DriverId,
            fixture.Booking.Id);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(BookingStatus.Completed, fixture.Booking.Status);
        Assert.Equal(otherBookingStatus, otherBooking.Status);
        Assert.Equal(RideStatus.Completed, fixture.Ride.Status);
        Assert.NotNull(fixture.Ride.CompletedAt);
        fixture.Rides.Verify(repository => repository.UpdateAsync(fixture.Ride), Times.Once);
    }

    [Fact]
    public async Task CancelActiveBooking_ApiRule_RejectsWithoutStateChange()
    {
        using var fixture = new ServiceFixture(BookingStatus.Active);

        var result = await fixture.PassengerService.CancelBookingAsync(
            fixture.Booking.PassengerId,
            fixture.Booking.Id,
            new CancelBookingDto { Reason = "My travel plans have changed." });

        Assert.False(result.IsSuccess);
        Assert.Equal("Only pending or accepted bookings can be cancelled.", result.Message);
        Assert.Equal(BookingStatus.Active, fixture.Booking.Status);
        Assert.Null(fixture.Booking.CancelReason);
        fixture.Notifications.Verify(
            service => service.RemovePassengerFromRideGroupsAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.Users.Verify(
            repository => repository.CreateCancellationLogAsync(It.IsAny<CancellationLog>()),
            Times.Never);
    }

    private sealed class ServiceFixture : IDisposable
    {
        public ServiceFixture(BookingStatus initialBookingStatus = BookingStatus.Accepted)
        {
            Context = new PakiCarpoolDbContext(
                new DbContextOptionsBuilder<PakiCarpoolDbContext>()
                    .UseInMemoryDatabase($"terminal-state-{Guid.NewGuid():N}")
                    .Options);

            var driverUser = new User
            {
                Id = DriverId,
                FullName = "Terminal Test Driver",
                Email = "driver@example.com",
                Role = UserRole.Driver
            };
            Profile = new DriverProfile
            {
                Id = Guid.NewGuid(),
                UserId = DriverId,
                User = driverUser,
                LivenessVerified = true
            };
            var car = new DriverCar
            {
                Id = Guid.NewGuid(),
                DriverId = Profile.Id,
                Driver = Profile,
                Make = "Test",
                Model = "Car",
                Year = 2026,
                LicensePlate = "TEST-PLATE",
                AvailableSeats = 4,
                Status = CarStatus.Approved
            };
            Ride = new Ride
            {
                Id = Guid.NewGuid(),
                DriverId = Profile.Id,
                Driver = Profile,
                CarId = car.Id,
                Car = car,
                Fare = 500,
                MaxSeats = 4,
                Status = RideStatus.Active,
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                CancelGraceExpiresAt = DateTime.UtcNow.AddMinutes(5)
            };
            Pickup = new RideStop
            {
                Id = Guid.NewGuid(),
                RideId = Ride.Id,
                Ride = Ride,
                Name = "Pickup",
                SequenceOrder = 1,
                Status = RideStopStatus.Completed
            };
            Dropoff = new RideStop
            {
                Id = Guid.NewGuid(),
                RideId = Ride.Id,
                Ride = Ride,
                Name = "Dropoff",
                SequenceOrder = 2
            };
            Ride.Stops = new List<RideStop> { Pickup, Dropoff };
            Context.AddRange(driverUser, Profile, car, Ride, Pickup, Dropoff);
            Context.SaveChanges();
            Booking = AddBooking(initialBookingStatus);

            var bookingRepository = new BookingRepository(
                Context,
                new SeatCapacityQuery(new Mock<IDapperConnectionFactory>().Object));
            Users.Setup(repository => repository.GetDriverProfileByUserIdAsync(
                    DriverId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Profile);
            Rides.Setup(repository => repository.GetByIdAsync(
                    Ride.Id,
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Ride);
            Rides.Setup(repository => repository.GetStopByIdAsync(
                    Dropoff.Id,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Dropoff);
            Rides.Setup(repository => repository.UpdateAsync(Ride))
                .Returns(() => Context.SaveChangesAsync());

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AES_KEY_V1"] = Convert.ToBase64String(new byte[32])
                })
                .Build();
            DriverService = new DriverService(
                Users.Object,
                Context,
                Rides.Object,
                bookingRepository,
                Notifications.Object,
                Flags.Object,
                Trust.Object,
                Email.Object,
                new AesEncryptionHelper(configuration),
                new Mock<IStorageService>().Object,
                HubContext.Object);
            PassengerService = new PassengerService(
                Users.Object,
                Context,
                Rides.Object,
                bookingRepository,
                Notifications.Object,
                Flags.Object,
                Trust.Object,
                Email.Object,
                HubContext.Object);
        }

        public Guid DriverId { get; } = Guid.NewGuid();
        public PakiCarpoolDbContext Context { get; }
        public DriverProfile Profile { get; }
        public Ride Ride { get; }
        public RideStop Pickup { get; }
        public RideStop Dropoff { get; }
        public Booking Booking { get; }
        public DriverService DriverService { get; }
        public PassengerService PassengerService { get; }
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IRideRepository> Rides { get; } = new();
        public Mock<INotificationService> Notifications { get; } = new();
        public Mock<IEmailService> Email { get; } = new();
        private Mock<IFlagService> Flags { get; } = new();
        private Mock<ITrustService> Trust { get; } = new();
        private Mock<IHubContext<Hub>> HubContext { get; } = new();

        public Booking AddBooking(BookingStatus status)
        {
            var passenger = new User
            {
                Id = Guid.NewGuid(),
                FullName = $"Passenger {status}",
                Email = $"{Guid.NewGuid():N}@example.com",
                Role = UserRole.Passenger
            };
            var booking = new Booking
            {
                Id = Guid.NewGuid(),
                RideId = Ride.Id,
                Ride = Ride,
                PassengerId = passenger.Id,
                Passenger = passenger,
                PickupStopId = Pickup.Id,
                PickupStop = Pickup,
                DropoffStopId = Dropoff.Id,
                DropoffStop = Dropoff,
                Status = status,
                RequestedAt = DateTime.UtcNow.AddMinutes(-1)
            };
            Context.AddRange(passenger, booking);
            Context.SaveChanges();
            return booking;
        }

        public void Dispose() => Context.Dispose();
    }
}
