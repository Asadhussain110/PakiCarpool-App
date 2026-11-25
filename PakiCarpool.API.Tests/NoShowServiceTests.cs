using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using PakiCarpool.Common.Constants;
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

/// <summary>
/// Regression coverage for backlog item 10: no-show outcomes.
/// Both report directions enforce the participant checks, the three-minute wait,
/// the en-route protection for drivers, and the once-per-no-show penalty rule.
/// At 4 no-shows an admin-review flag is raised — suspension is an admin decision.
/// </summary>
public sealed class NoShowServiceTests
{
    // ── Passenger no-show (reported by the driver) ───────────────────────────

    [Fact]
    public async Task PassengerNoShow_AfterThreeMinuteWait_IsRecordedAtomically()
    {
        using var fixture = new ServiceFixture();
        fixture.Booking.Status = BookingStatus.Active;
        fixture.Pickup.Status = RideStopStatus.Completed;
        fixture.Pickup.ArrivedAt = fixture.Now.AddMinutes(-AppConstants.NoShowWaitMinutes - 2);

        var result = await fixture.DriverService.MarkPassengerNoShowAsync(
            fixture.DriverUserId, fixture.Booking.Id);

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotNull(fixture.RecordedLog);
        Assert.Equal(fixture.DriverUserId, fixture.RecordedLog!.ReporterId);
        Assert.Equal(fixture.PassengerId, fixture.RecordedLog.OffenderId);
        Assert.Equal(fixture.Ride.Id, fixture.RecordedLog.RideId);
        Assert.Equal(fixture.Booking.Id, fixture.RecordedLog.BookingId);
        Assert.Equal(AppConstants.NoShowPenalty, fixture.RecordedPenalty);
        Assert.Equal(AppConstants.NoShowPassengerCancelReason, fixture.RecordedCancelReason);
        Assert.Contains(BookingStatus.Accepted, fixture.RecordedStatuses!);
        Assert.Contains(BookingStatus.Active, fixture.RecordedStatuses!);
        fixture.Bookings.Verify(repository => repository.RecordNoShowAsync(
            It.IsAny<NoShowLog>(),
            It.IsAny<IReadOnlyCollection<BookingStatus>>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Once);
        // Offender notification (in-app + email) and threshold recheck fire exactly once.
        fixture.Notifications.Verify(service => service.SendInAppNotificationAsync(
            fixture.PassengerId, NotificationType.NoShowRecorded, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        fixture.Email.Verify(service => service.SendNoShowEmailAsync(
            fixture.Passenger.Email, fixture.Passenger.FullName), Times.Once);
        fixture.Trust.Verify(service => service.CheckThresholdsAsync(fixture.PassengerId), Times.Once);
        fixture.Notifications.Verify(service => service.RemovePassengerFromRideGroupsAsync(
            fixture.Ride.Id, fixture.PassengerId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PassengerNoShow_BeforeThreeMinutesElapsed_Rejected()
    {
        using var fixture = new ServiceFixture();
        fixture.Booking.Status = BookingStatus.Active;
        fixture.Pickup.Status = RideStopStatus.Completed;
        fixture.Pickup.ArrivedAt = fixture.Now.AddMinutes(-1);

        var result = await fixture.DriverService.MarkPassengerNoShowAsync(
            fixture.DriverUserId, fixture.Booking.Id);

        Assert.False(result.IsSuccess);
        AssertNoSideEffects(fixture);
    }

    [Fact]
    public async Task PassengerNoShow_BeforeDriverArrivalAtPickup_Rejected()
    {
        using var fixture = new ServiceFixture();
        fixture.Booking.Status = BookingStatus.Active;
        fixture.Pickup.Status = RideStopStatus.Active;

        var result = await fixture.DriverService.MarkPassengerNoShowAsync(
            fixture.DriverUserId, fixture.Booking.Id);

        Assert.False(result.IsSuccess);
        AssertNoSideEffects(fixture);
    }

    [Fact]
    public async Task PassengerNoShow_ByNonParticipantDriver_Rejected()
    {
        using var fixture = new ServiceFixture();
        fixture.Booking.Status = BookingStatus.Active;
        fixture.Pickup.Status = RideStopStatus.Completed;
        fixture.Pickup.ArrivedAt = fixture.Now.AddMinutes(-10);

        var result = await fixture.DriverService.MarkPassengerNoShowAsync(
            fixture.OutsiderDriverUserId, fixture.Booking.Id);

        Assert.False(result.IsSuccess);
        AssertNoSideEffects(fixture);
    }

    [Fact]
    public async Task PassengerNoShow_DuplicateReport_DoesNotDoublePenalize()
    {
        using var fixture = new ServiceFixture();
        fixture.Booking.Status = BookingStatus.Active;
        fixture.Pickup.Status = RideStopStatus.Completed;
        fixture.Pickup.ArrivedAt = fixture.Now.AddMinutes(-10);
        fixture.RecordOutcome = NoShowRecordingOutcome.AlreadyReported;

        var result = await fixture.DriverService.MarkPassengerNoShowAsync(
            fixture.DriverUserId, fixture.Booking.Id);

        Assert.False(result.IsSuccess);
        // Exactly one recording attempt, and no penalty/notifications on the duplicate.
        fixture.Bookings.Verify(repository => repository.RecordNoShowAsync(
            It.IsAny<NoShowLog>(),
            It.IsAny<IReadOnlyCollection<BookingStatus>>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Notifications.Verify(service => service.SendInAppNotificationAsync(
            It.IsAny<Guid>(), It.IsAny<NotificationType>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        fixture.Email.Verify(service => service.SendNoShowEmailAsync(
            It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        fixture.Trust.Verify(service => service.CheckThresholdsAsync(It.IsAny<Guid>()), Times.Never);
    }

    // ── Driver no-show (reported by the passenger) ───────────────────────────

    [Fact]
    public async Task DriverNoShow_AfterWindowWithStaleGps_IsRecordedAndCancelsBooking()
    {
        using var fixture = new ServiceFixture();
        // Booking Accepted 5 minutes ago, no GPS from the driver at all.
        fixture.Location = null;

        var result = await fixture.PassengerService.MarkDriverNoShowAsync(
            fixture.PassengerId, fixture.Booking.Id);

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotNull(fixture.RecordedLog);
        Assert.Equal(fixture.PassengerId, fixture.RecordedLog!.ReporterId);
        Assert.Equal(fixture.DriverUserId, fixture.RecordedLog.OffenderId);
        Assert.Equal(fixture.Ride.Id, fixture.RecordedLog.RideId);
        Assert.Equal(fixture.Booking.Id, fixture.RecordedLog.BookingId);
        Assert.Equal(AppConstants.NoShowPenalty, fixture.RecordedPenalty);
        Assert.Equal(AppConstants.NoShowDriverCancelReason, fixture.RecordedCancelReason);
        // Only an Accepted booking is eligible in this direction.
        Assert.Single(fixture.RecordedStatuses!, BookingStatus.Accepted);
        fixture.Bookings.Verify(repository => repository.RecordNoShowAsync(
            It.IsAny<NoShowLog>(),
            It.IsAny<IReadOnlyCollection<BookingStatus>>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Once);
        // The passenger is informed of the cancellation; the driver is the offender.
        fixture.Notifications.Verify(service => service.SendInAppNotificationAsync(
            fixture.PassengerId, NotificationType.BookingAutoCancelled, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        fixture.Notifications.Verify(service => service.SendInAppNotificationAsync(
            fixture.DriverUserId, NotificationType.NoShowRecorded, It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        fixture.Email.Verify(service => service.SendNoShowEmailAsync(
            fixture.DriverUser.Email, fixture.DriverUser.FullName), Times.Once);
        // Thresholds are rechecked for the offender only — no passenger penalty.
        fixture.Trust.Verify(service => service.CheckThresholdsAsync(fixture.DriverUserId), Times.Once);
        fixture.Trust.Verify(service => service.CheckThresholdsAsync(fixture.PassengerId), Times.Never);
        fixture.Trust.Verify(service => service.DeductPointsAsync(It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
        fixture.Notifications.Verify(service => service.RemovePassengerFromRideGroupsAsync(
            fixture.Ride.Id, fixture.PassengerId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DriverNoShow_BeforeAcceptedAtPlusThreeMinutes_Rejected()
    {
        using var fixture = new ServiceFixture();
        fixture.Booking.AcceptedAt = fixture.Now.AddMinutes(-1);
        fixture.Location = null;

        var result = await fixture.PassengerService.MarkDriverNoShowAsync(
            fixture.PassengerId, fixture.Booking.Id);

        Assert.False(result.IsSuccess);
        AssertNoSideEffects(fixture);
    }

    [Fact]
    public async Task DriverNoShow_WhenDriverArrivedAtPickup_Rejected()
    {
        using var fixture = new ServiceFixture();
        fixture.Pickup.Status = RideStopStatus.Completed;
        fixture.Pickup.ArrivedAt = fixture.Now.AddMinutes(-1);

        var result = await fixture.PassengerService.MarkDriverNoShowAsync(
            fixture.PassengerId, fixture.Booking.Id);

        Assert.False(result.IsSuccess);
        AssertNoSideEffects(fixture);
    }

    [Fact]
    public async Task DriverNoShow_WhenDriverEnRouteWithFreshGps_Rejected()
    {
        using var fixture = new ServiceFixture();
        fixture.Location = new DriverLocation
        {
            Id = Guid.NewGuid(),
            DriverId = fixture.Profile.Id,
            RideId = fixture.Ride.Id,
            Latitude = 24.86,
            Longitude = 67.01,
            RecordedAt = fixture.Now.AddSeconds(-30)
        };

        var result = await fixture.PassengerService.MarkDriverNoShowAsync(
            fixture.PassengerId, fixture.Booking.Id);

        Assert.False(result.IsSuccess);
        AssertNoSideEffects(fixture);
    }

    [Fact]
    public async Task DriverNoShow_WhenGpsStale_AfterWindow_IsAllowed()
    {
        using var fixture = new ServiceFixture();
        fixture.Location = new DriverLocation
        {
            Id = Guid.NewGuid(),
            DriverId = fixture.Profile.Id,
            RideId = fixture.Ride.Id,
            Latitude = 24.86,
            Longitude = 67.01,
            RecordedAt = fixture.Now.AddMinutes(-(AppConstants.NoShowGpsFreshnessMinutes + 2))
        };

        var result = await fixture.PassengerService.MarkDriverNoShowAsync(
            fixture.PassengerId, fixture.Booking.Id);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(fixture.DriverUserId, fixture.RecordedLog!.OffenderId);
    }

    [Fact]
    public async Task DriverNoShow_WhenDriverSkippedPickup_EvenWithFreshGps_IsAllowed()
    {
        using var fixture = new ServiceFixture();
        var skipped = new RideStop
        {
            Id = Guid.NewGuid(),
            RideId = fixture.Ride.Id,
            Ride = fixture.Ride,
            Name = "Later Stop",
            SequenceOrder = fixture.Pickup.SequenceOrder + 1,
            Status = RideStopStatus.Completed,
            ArrivedAt = fixture.Now.AddMinutes(-1)
        };
        fixture.Ride.Stops = new List<RideStop> { fixture.Pickup, skipped };
        fixture.Location = new DriverLocation
        {
            Id = Guid.NewGuid(),
            DriverId = fixture.Profile.Id,
            RideId = fixture.Ride.Id,
            Latitude = 24.86,
            Longitude = 67.01,
            RecordedAt = fixture.Now.AddSeconds(-30)
        };

        var result = await fixture.PassengerService.MarkDriverNoShowAsync(
            fixture.PassengerId, fixture.Booking.Id);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(fixture.DriverUserId, fixture.RecordedLog!.OffenderId);
    }

    [Fact]
    public async Task DriverNoShow_OnCancelledRide_Rejected()
    {
        using var fixture = new ServiceFixture();
        fixture.Ride.Status = RideStatus.Cancelled;
        fixture.Location = null;

        var result = await fixture.PassengerService.MarkDriverNoShowAsync(
            fixture.PassengerId, fixture.Booking.Id);

        Assert.False(result.IsSuccess);
        AssertNoSideEffects(fixture);
    }

    [Fact]
    public async Task DriverNoShow_ByNonParticipant_Rejected()
    {
        using var fixture = new ServiceFixture();
        fixture.Location = null;

        var result = await fixture.PassengerService.MarkDriverNoShowAsync(
            fixture.OutsiderPassengerId, fixture.Booking.Id);

        Assert.False(result.IsSuccess);
        AssertNoSideEffects(fixture);
    }

    [Fact]
    public async Task DriverNoShow_RepeatReport_ChangesNothing()
    {
        using var fixture = new ServiceFixture();
        fixture.Location = null;
        fixture.RecordOutcome = NoShowRecordingOutcome.AlreadyReported;

        var result = await fixture.PassengerService.MarkDriverNoShowAsync(
            fixture.PassengerId, fixture.Booking.Id);

        Assert.False(result.IsSuccess);
        fixture.Bookings.Verify(repository => repository.RecordNoShowAsync(
            It.IsAny<NoShowLog>(),
            It.IsAny<IReadOnlyCollection<BookingStatus>>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Notifications.Verify(service => service.SendInAppNotificationAsync(
            It.IsAny<Guid>(), It.IsAny<NotificationType>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        fixture.Trust.Verify(service => service.CheckThresholdsAsync(It.IsAny<Guid>()), Times.Never);
    }

    private static void AssertNoSideEffects(ServiceFixture fixture)
    {
        fixture.Bookings.Verify(repository => repository.RecordNoShowAsync(
            It.IsAny<NoShowLog>(),
            It.IsAny<IReadOnlyCollection<BookingStatus>>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.Notifications.Verify(service => service.SendInAppNotificationAsync(
            It.IsAny<Guid>(), It.IsAny<NotificationType>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        fixture.Email.Verify(service => service.SendNoShowEmailAsync(
            It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        fixture.Trust.Verify(service => service.CheckThresholdsAsync(It.IsAny<Guid>()), Times.Never);
        fixture.Notifications.Verify(service => service.RemovePassengerFromRideGroupsAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class ServiceFixture : IDisposable
    {
        public ServiceFixture()
        {
            Now = DateTime.UtcNow;
            Context = new PakiCarpoolDbContext(
                new DbContextOptionsBuilder<PakiCarpoolDbContext>()
                    .UseInMemoryDatabase($"no-show-{Guid.NewGuid():N}")
                    .Options);

            DriverUserId = Guid.NewGuid();
            PassengerId = Guid.NewGuid();
            OutsiderPassengerId = Guid.NewGuid();
            OutsiderDriverUserId = Guid.NewGuid();
            ProfileId = Guid.NewGuid();

            DriverUser = new User
            {
                Id = DriverUserId,
                FullName = "No-Show Driver",
                Email = "noshow-driver@example.com",
                Role = UserRole.Driver,
                TrustScore = AppConstants.TrustScoreInitial,
                IsActive = true
            };
            Passenger = new User
            {
                Id = PassengerId,
                FullName = "No-Show Passenger",
                Email = "noshow-passenger@example.com",
                Role = UserRole.Passenger,
                TrustScore = AppConstants.TrustScoreInitial,
                IsActive = true
            };
            var outsiderPassenger = new User
            {
                Id = OutsiderPassengerId,
                FullName = "Outsider Passenger",
                Email = "noshow-outsider@example.com",
                Role = UserRole.Passenger,
                TrustScore = AppConstants.TrustScoreInitial,
                IsActive = true
            };
            var outsiderDriverUser = new User
            {
                Id = OutsiderDriverUserId,
                FullName = "Outsider Driver",
                Email = "noshow-outsider-driver@example.com",
                Role = UserRole.Driver,
                TrustScore = AppConstants.TrustScoreInitial,
                IsActive = true
            };
            Profile = new DriverProfile
            {
                Id = ProfileId,
                UserId = DriverUserId,
                User = DriverUser,
                LivenessVerified = true
            };
            OutsiderProfile = new DriverProfile
            {
                Id = Guid.NewGuid(),
                UserId = OutsiderDriverUserId,
                User = outsiderDriverUser,
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
                LicensePlate = "NOSHOW-1",
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
                Status = RideStatus.Active,
                CreatedAt = Now.AddMinutes(-20),
                CancelGraceExpiresAt = Now.AddMinutes(-15)
            };
            Pickup = new RideStop
            {
                Id = Guid.NewGuid(),
                RideId = Ride.Id,
                Ride = Ride,
                Name = "Pickup",
                SequenceOrder = 1,
                Status = RideStopStatus.Upcoming
            };
            Dropoff = new RideStop
            {
                Id = Guid.NewGuid(),
                RideId = Ride.Id,
                Ride = Ride,
                Name = "Dropoff",
                SequenceOrder = 2,
                Status = RideStopStatus.Upcoming
            };
            Ride.Stops = new List<RideStop> { Pickup, Dropoff };
            Booking = new Booking
            {
                Id = Guid.NewGuid(),
                RideId = Ride.Id,
                Ride = Ride,
                PassengerId = PassengerId,
                Passenger = Passenger,
                PickupStopId = Pickup.Id,
                PickupStop = Pickup,
                DropoffStopId = Dropoff.Id,
                DropoffStop = Dropoff,
                Status = BookingStatus.Accepted,
                RequestedAt = Now.AddMinutes(-10),
                AcceptedAt = Now.AddMinutes(-5)
            };
            Context.AddRange(DriverUser, Passenger, outsiderPassenger, outsiderDriverUser, Profile, OutsiderProfile, car, Ride, Pickup, Dropoff, Booking);
            Context.SaveChanges();

            var usersById = new Dictionary<Guid, User>
            {
                [DriverUser.Id] = DriverUser,
                [Passenger.Id] = Passenger,
                [outsiderPassenger.Id] = outsiderPassenger,
                [outsiderDriverUser.Id] = outsiderDriverUser
            };
            Users.Setup(repository => repository.GetByIdAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => usersById.GetValueOrDefault(id));
            Users.Setup(repository => repository.GetDriverProfileByUserIdAsync(
                    DriverUserId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Profile);
            Users.Setup(repository => repository.GetDriverProfileByUserIdAsync(
                    OutsiderDriverUserId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(OutsiderProfile);
            Users.Setup(repository => repository.GetDriverProfileByIdAsync(
                    ProfileId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Profile);
            Rides.Setup(repository => repository.GetByIdAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Ride);
            Rides.Setup(repository => repository.GetStopByIdAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid stopId, CancellationToken _) =>
                    stopId == Pickup.Id ? Pickup : stopId == Dropoff.Id ? Dropoff : null);
            Rides.Setup(repository => repository.GetDriverLocationAsync(
                    ProfileId,
                    Ride.Id))
                .Returns(() => Task.FromResult(Location));
            Bookings.Setup(repository => repository.GetByIdAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, bool _, bool _, CancellationToken _) =>
                    id == Booking.Id ? Booking : null);
            Bookings.Setup(repository => repository.RecordNoShowAsync(
                    It.IsAny<NoShowLog>(),
                    It.IsAny<IReadOnlyCollection<BookingStatus>>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
                .Callback<NoShowLog, IReadOnlyCollection<BookingStatus>, string, int, DateTime, CancellationToken>(
                    (log, statuses, cancelReason, penalty, _, _) =>
                    {
                        RecordedLog = log;
                        RecordedStatuses = statuses;
                        RecordedCancelReason = cancelReason;
                        RecordedPenalty = penalty;
                    })
                .Returns(() => Task.FromResult(RecordOutcome));

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AES_KEY_V1"] = Convert.ToBase64String(new byte[32])
                })
                .Build();
            var timeProvider = new FixedTimeProvider(Now);
            DriverService = new DriverService(
                Users.Object,
                Context,
                Rides.Object,
                Bookings.Object,
                Notifications.Object,
                Flags.Object,
                Trust.Object,
                Email.Object,
                new AesEncryptionHelper(configuration),
                new Mock<IStorageService>().Object,
                HubContext.Object,
                timeProvider);
            PassengerService = new PassengerService(
                Users.Object,
                Context,
                Rides.Object,
                Bookings.Object,
                Notifications.Object,
                Flags.Object,
                Trust.Object,
                Email.Object,
                HubContext.Object,
                timeProvider);
        }

        public DateTime Now { get; }
        public PakiCarpoolDbContext Context { get; }
        public Guid DriverUserId { get; }
        public Guid PassengerId { get; }
        public Guid OutsiderPassengerId { get; }
        public Guid OutsiderDriverUserId { get; }
        public Guid ProfileId { get; }
        public User DriverUser { get; }
        public User Passenger { get; }
        public DriverProfile Profile { get; }
        public DriverProfile OutsiderProfile { get; }
        public Ride Ride { get; }
        public RideStop Pickup { get; }
        public RideStop Dropoff { get; }
        public Booking Booking { get; }
        public DriverLocation? Location { get; set; }
        public NoShowRecordingOutcome RecordOutcome { get; set; } = NoShowRecordingOutcome.Recorded;
        public NoShowLog? RecordedLog { get; private set; }
        public IReadOnlyCollection<BookingStatus>? RecordedStatuses { get; private set; }
        public string? RecordedCancelReason { get; private set; }
        public int RecordedPenalty { get; private set; }
        public DriverService DriverService { get; }
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
