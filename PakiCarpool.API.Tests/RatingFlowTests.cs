using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;
using PakiCarpool.Domain.DTOs.Common;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Repository.EFCore;
using PakiCarpool.Repository.EFCore.Repositories;
using PakiCarpool.Services.Implementations;
using Xunit;

namespace PakiCarpool.API.Tests;

/// <summary>
/// Regression coverage for backlog item 9: the rating API resolves rateeId
/// through Users.Id, while ride DTOs expose DriverId = DriverProfiles.Id.
/// The ride DTO now carries DriverUserId explicitly and the mobile passenger
/// app submits that value, so both rating directions use user IDs end to end.
/// </summary>
public sealed class RatingFlowTests
{
    [Fact]
    public async Task PassengerRatesDriver_WithRideDtoDriverUserId_SubmitsRating()
    {
        using var fixture = new ServiceFixture();

        var result = await fixture.RatingService.SubmitRatingAsync(
            fixture.PassengerId,
            new RatingDto
            {
                RideId = fixture.Ride.Id,
                RateeId = fixture.DriverUserId,
                Stars = 5,
                ReviewText = "Great ride"
            });

        Assert.True(result.IsSuccess, result.Message);
        var rating = Assert.Single(fixture.Context.Ratings.ToList());
        Assert.Equal(fixture.Ride.Id, rating.RideId);
        Assert.Equal(fixture.PassengerId, rating.RaterId);
        Assert.Equal(fixture.DriverUserId, rating.RateeId);
        // Trust points go to the driver's user account...
        fixture.Trust.Verify(service =>
            service.AddPointsAsync(fixture.DriverUserId, It.IsAny<int>()), Times.Once);
        // ...while the driver rating average is recalculated from the profile ID.
        fixture.Trust.Verify(service =>
            service.RecalculateDriverRatingAsync(fixture.ProfileId), Times.Once);
    }

    [Fact]
    public async Task DriverRatesPassenger_WithBookingPassengerUserId_SubmitsRating()
    {
        using var fixture = new ServiceFixture();

        var result = await fixture.RatingService.SubmitRatingAsync(
            fixture.DriverUserId,
            new RatingDto
            {
                RideId = fixture.Ride.Id,
                RateeId = fixture.PassengerId,
                Stars = 4
            });

        Assert.True(result.IsSuccess, result.Message);
        var rating = Assert.Single(fixture.Context.Ratings.ToList());
        Assert.Equal(fixture.DriverUserId, rating.RaterId);
        Assert.Equal(fixture.PassengerId, rating.RateeId);
        fixture.Trust.Verify(service =>
            service.AddPointsAsync(fixture.PassengerId, It.IsAny<int>()), Times.Once);
        // The ratee is a passenger, so no driver average recalculation happens.
        fixture.Trust.Verify(service =>
            service.RecalculateDriverRatingAsync(It.IsAny<Guid>()), Times.Never);
    }

    /// <summary>
    /// The pre-fix client behavior: submitting the ride DTO's DriverId
    /// (a driver-profile ID) as rateeId must be rejected, not silently remapped.
    /// </summary>
    [Fact]
    public async Task PassengerRatesDriver_WithDriverProfileIdAsRatee_Rejected()
    {
        using var fixture = new ServiceFixture();

        var result = await fixture.RatingService.SubmitRatingAsync(
            fixture.PassengerId,
            new RatingDto
            {
                RideId = fixture.Ride.Id,
                RateeId = fixture.ProfileId,
                Stars = 5
            });

        Assert.False(result.IsSuccess);
        Assert.Empty(fixture.Context.Ratings.ToList());
    }

    [Fact]
    public async Task Rating_WithUnknownRateeId_Rejected()
    {
        using var fixture = new ServiceFixture();

        var result = await fixture.RatingService.SubmitRatingAsync(
            fixture.PassengerId,
            new RatingDto
            {
                RideId = fixture.Ride.Id,
                RateeId = Guid.NewGuid(),
                Stars = 3
            });

        Assert.False(result.IsSuccess);
        Assert.Empty(fixture.Context.Ratings.ToList());
    }

    [Fact]
    public async Task UsersNotPartOfRide_CannotRateItsParticipants()
    {
        using var fixture = new ServiceFixture();

        // An outsider with no booking on the ride cannot rate the driver...
        var outsiderRatesDriver = await fixture.RatingService.SubmitRatingAsync(
            fixture.OutsiderId,
            new RatingDto
            {
                RideId = fixture.Ride.Id,
                RateeId = fixture.DriverUserId,
                Stars = 5
            });
        Assert.False(outsiderRatesDriver.IsSuccess);

        // ...and the passenger cannot rate a user who was not on the ride.
        var passengerRatesOutsider = await fixture.RatingService.SubmitRatingAsync(
            fixture.PassengerId,
            new RatingDto
            {
                RideId = fixture.Ride.Id,
                RateeId = fixture.OutsiderId,
                Stars = 5
            });
        Assert.False(passengerRatesOutsider.IsSuccess);

        Assert.Empty(fixture.Context.Ratings.ToList());
    }

    [Fact]
    public async Task RideDetail_ExposesDistinctDriverProfileAndUserIds()
    {
        using var fixture = new ServiceFixture(RideStatus.Active);

        var result = await fixture.PassengerService.GetRideDetailAsync(fixture.Ride.Id);

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotNull(result.Data);
        // DriverId stays the driver-profile ID for existing consumers;
        // DriverUserId is the user ID the rating contract expects.
        Assert.Equal(fixture.ProfileId, result.Data!.DriverId);
        Assert.Equal(fixture.DriverUserId, result.Data.DriverUserId);
        Assert.NotEqual(result.Data.DriverId, result.Data.DriverUserId);
    }

    private sealed class ServiceFixture : IDisposable
    {
        public ServiceFixture(RideStatus rideStatus = RideStatus.Completed)
        {
            Context = new PakiCarpoolDbContext(
                new DbContextOptionsBuilder<PakiCarpoolDbContext>()
                    .UseInMemoryDatabase($"rating-flow-{Guid.NewGuid():N}")
                    .Options);

            DriverUserId = Guid.NewGuid();
            PassengerId = Guid.NewGuid();
            OutsiderId = Guid.NewGuid();
            ProfileId = Guid.NewGuid();

            var driverUser = new User
            {
                Id = DriverUserId,
                FullName = "Rating Driver",
                Email = "rating-driver@example.com",
                Role = UserRole.Driver
            };
            Passenger = new User
            {
                Id = PassengerId,
                FullName = "Rating Passenger",
                Email = "rating-passenger@example.com",
                Role = UserRole.Passenger
            };
            Outsider = new User
            {
                Id = OutsiderId,
                FullName = "Rating Outsider",
                Email = "rating-outsider@example.com",
                Role = UserRole.Passenger
            };
            Profile = new DriverProfile
            {
                Id = ProfileId,
                UserId = DriverUserId,
                User = driverUser,
                LivenessVerified = true
            };
            var car = new DriverCar
            {
                Id = Guid.NewGuid(),
                DriverId = ProfileId,
                Driver = Profile,
                Make = "Test",
                Model = "Car",
                Year = 2026,
                LicensePlate = "RATING-1",
                AvailableSeats = 4,
                Status = CarStatus.Approved
            };
            Ride = new Ride
            {
                Id = Guid.NewGuid(),
                DriverId = ProfileId,
                Driver = Profile,
                CarId = car.Id,
                Car = car,
                Fare = 500,
                MaxSeats = 4,
                Status = rideStatus,
                CreatedAt = DateTime.UtcNow.AddMinutes(-30)
            };
            var pickup = new RideStop
            {
                Id = Guid.NewGuid(),
                RideId = Ride.Id,
                Ride = Ride,
                Name = "Pickup",
                SequenceOrder = 1,
                Status = RideStopStatus.Completed
            };
            var dropoff = new RideStop
            {
                Id = Guid.NewGuid(),
                RideId = Ride.Id,
                Ride = Ride,
                Name = "Dropoff",
                SequenceOrder = 2,
                Status = RideStopStatus.Completed
            };
            Ride.Stops = new List<RideStop> { pickup, dropoff };
            Booking = new Booking
            {
                Id = Guid.NewGuid(),
                RideId = Ride.Id,
                Ride = Ride,
                PassengerId = PassengerId,
                Passenger = Passenger,
                PickupStopId = pickup.Id,
                PickupStop = pickup,
                DropoffStopId = dropoff.Id,
                DropoffStop = dropoff,
                Status = BookingStatus.Completed,
                RequestedAt = DateTime.UtcNow.AddMinutes(-25),
                IsCashPaid = true
            };
            Ride.Bookings = new List<Booking> { Booking };
            Context.AddRange(driverUser, Passenger, Outsider, Profile, car, Ride, pickup, dropoff, Booking);
            Context.SaveChanges();

            var usersById = new Dictionary<Guid, User>
            {
                [driverUser.Id] = driverUser,
                [Passenger.Id] = Passenger,
                [Outsider.Id] = Outsider
            };
            Users.Setup(repository => repository.GetByIdAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) =>
                    usersById.GetValueOrDefault(id));
            Users.Setup(repository => repository.GetDriverProfileByUserIdAsync(
                    DriverUserId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Profile);
            Users.Setup(repository => repository.GetDriverProfileByIdAsync(
                    ProfileId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Profile);
            Rides.Setup(repository => repository.GetByIdAsync(
                    Ride.Id,
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Ride);
            Bookings.Setup(repository => repository.GetBookingsByRideIdAsync(
                    Ride.Id,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Booking> { Booking });

            RatingService = new RatingService(
                new AdminRepository(Context),
                Users.Object,
                Rides.Object,
                Bookings.Object,
                Trust.Object);
            PassengerService = new PassengerService(
                Users.Object,
                Context,
                Rides.Object,
                Bookings.Object,
                Notifications.Object,
                Flags.Object,
                Trust.Object,
                Email.Object,
                HubContext.Object);
        }

        public Guid DriverUserId { get; }
        public Guid PassengerId { get; }
        public Guid OutsiderId { get; }
        public Guid ProfileId { get; }
        public PakiCarpoolDbContext Context { get; }
        public DriverProfile Profile { get; }
        public Ride Ride { get; }
        public Booking Booking { get; }
        public User Passenger { get; }
        public User Outsider { get; }
        public RatingService RatingService { get; }
        public PassengerService PassengerService { get; }
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IRideRepository> Rides { get; } = new();
        public Mock<IBookingRepository> Bookings { get; } = new();
        public Mock<ITrustService> Trust { get; } = new();
        public Mock<INotificationService> Notifications { get; } = new();
        public Mock<IFlagService> Flags { get; } = new();
        public Mock<IEmailService> Email { get; } = new();
        public Mock<IHubContext<Hub>> HubContext { get; } = new();

        public void Dispose() => Context.Dispose();
    }
}
