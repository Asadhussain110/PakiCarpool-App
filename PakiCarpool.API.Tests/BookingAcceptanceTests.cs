using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Repository.EFCore;
using PakiCarpool.Security.Helpers;
using PakiCarpool.Services.BackgroundJobs;
using PakiCarpool.Services.Implementations;
using Xunit;

namespace PakiCarpool.API.Tests;

public sealed class BookingAcceptanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AcceptBooking_CancelledRide_IsRejected()
    {
        var fixture = new ServiceFixture(BookingAcceptanceOutcome.RideCancelled);

        var result = await fixture.Service.AcceptBookingAsync(fixture.DriverId, fixture.Booking.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("Ride is cancelled and cannot accept bookings.", result.Message);
        fixture.VerifyNoAcceptanceNotification();
    }

    [Fact]
    public async Task AcceptBooking_CompletedRide_IsRejected()
    {
        var fixture = new ServiceFixture(BookingAcceptanceOutcome.RideCompleted);

        var result = await fixture.Service.AcceptBookingAsync(fixture.DriverId, fixture.Booking.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("Ride is completed and cannot accept bookings.", result.Message);
        fixture.VerifyNoAcceptanceNotification();
    }

    [Fact]
    public async Task AcceptBooking_AlreadyAcceptedBooking_IsRejected()
    {
        var fixture = new ServiceFixture(BookingAcceptanceOutcome.AlreadyAccepted);

        var result = await fixture.Service.AcceptBookingAsync(fixture.DriverId, fixture.Booking.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("Booking has already been accepted.", result.Message);
        fixture.VerifyNoAcceptanceNotification();
    }

    [Fact]
    public async Task AcceptBooking_AlreadyCancelledByTimeout_IsRejectedWithoutAnotherTimeoutNotification()
    {
        var fixture = new ServiceFixture(BookingAcceptanceOutcome.Cancelled);

        var result = await fixture.Service.AcceptBookingAsync(fixture.DriverId, fixture.Booking.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("Booking was cancelled and can no longer be accepted.", result.Message);
        fixture.VerifyNoAcceptanceNotification();
        fixture.Notifications.Verify(
            service => service.SendBookingAutoCancelledAsync(It.IsAny<Guid>(), It.IsAny<Guid>()),
            Times.Never);
        fixture.Notifications.Verify(
            service => service.SendNotificationAsync(
                It.IsAny<Guid>(),
                NotificationType.BookingAutoCancelled,
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task AcceptBooking_ExpiredPendingBooking_CancelsAndSendsOnlyTimeoutNotifications()
    {
        var fixture = new ServiceFixture(BookingAcceptanceOutcome.TimedOut);

        var result = await fixture.Service.AcceptBookingAsync(fixture.DriverId, fixture.Booking.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("Booking has timed out and can no longer be accepted.", result.Message);
        fixture.VerifyNoAcceptanceNotification();
        fixture.Notifications.Verify(
            service => service.SendBookingAutoCancelledAsync(
                fixture.Booking.PassengerId,
                fixture.Booking.Id),
            Times.Once);
        fixture.Notifications.Verify(
            service => service.SendNotificationAsync(
                fixture.Booking.PassengerId,
                NotificationType.BookingAutoCancelled,
                "Booking Timed Out",
                It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task TimeoutJob_WhenAnotherOperationAlreadyChangedBooking_DoesNotNotifyAgain()
    {
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            RideId = Guid.NewGuid(),
            PassengerId = Guid.NewGuid(),
            RequestedAt = Now.UtcDateTime.AddMinutes(-3),
            Status = BookingStatus.Pending
        };
        var repository = new Mock<IBookingRepository>();
        repository.Setup(r => r.GetTimedOutPendingBookingsAsync(Now.UtcDateTime.AddMinutes(-2)))
            .ReturnsAsync([booking]);
        repository.Setup(r => r.CancelTimedOutAsync(
                booking.Id,
                Now.UtcDateTime.AddMinutes(-2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Booking?)null);
        var notifications = new Mock<INotificationService>();
        using var provider = new ServiceCollection()
            .AddSingleton(repository.Object)
            .AddSingleton(notifications.Object)
            .BuildServiceProvider();
        var job = new BookingTimeoutJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BookingTimeoutJob>.Instance,
            new FixedTimeProvider(Now));

        await job.ProcessTimeoutsAsync(CancellationToken.None);

        repository.Verify(r => r.UpdateAsync(It.IsAny<Booking>()), Times.Never);
        notifications.Verify(
            service => service.SendBookingAutoCancelledAsync(It.IsAny<Guid>(), It.IsAny<Guid>()),
            Times.Never);
        notifications.Verify(
            service => service.SendNotificationAsync(
                It.IsAny<Guid>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    private sealed class ServiceFixture
    {
        public ServiceFixture(BookingAcceptanceOutcome outcome)
        {
            Profile = new DriverProfile { Id = Guid.NewGuid(), UserId = DriverId };
            Booking = new Booking
            {
                Id = Guid.NewGuid(),
                RideId = Guid.NewGuid(),
                PassengerId = Guid.NewGuid(),
                Status = BookingStatus.Pending
            };
            var ride = new Ride
            {
                Id = Booking.RideId,
                DriverId = Profile.Id,
                Status = RideStatus.Posted
            };
            Booking.Ride = ride;

            Users.Setup(r => r.GetDriverProfileByUserIdAsync(
                    DriverId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Profile);
            Bookings.Setup(r => r.GetByIdAsync(
                    Booking.Id,
                    true,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Booking);
            Rides.Setup(r => r.GetByIdAsync(
                    ride.Id,
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ride);
            Bookings.Setup(r => r.AcceptPendingAsync(
                    Booking.Id,
                    Now.UtcDateTime,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((outcome, Booking));

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AES_KEY_V1"] = Convert.ToBase64String(new byte[32])
                })
                .Build();
            Service = new DriverService(
                Users.Object,
                Context.Object,
                Rides.Object,
                Bookings.Object,
                Notifications.Object,
                Flags.Object,
                Trust.Object,
                Email.Object,
                new AesEncryptionHelper(configuration),
                new Mock<IStorageService>().Object,
                HubContext.Object,
                new FixedTimeProvider(Now));
        }

        public Guid DriverId { get; } = Guid.NewGuid();
        public DriverProfile Profile { get; }
        public Booking Booking { get; }
        public DriverService Service { get; }
        public Mock<INotificationService> Notifications { get; } = new();
        private Mock<IUserRepository> Users { get; } = new();
        private Mock<IRideRepository> Rides { get; } = new();
        private Mock<IBookingRepository> Bookings { get; } = new();
        private Mock<IFlagService> Flags { get; } = new();
        private Mock<ITrustService> Trust { get; } = new();
        private Mock<IEmailService> Email { get; } = new();
        private Mock<IHubContext<Hub>> HubContext { get; } = new();
        private Mock<PakiCarpoolDbContext> Context { get; } = new(
            new DbContextOptionsBuilder<PakiCarpoolDbContext>()
                .UseNpgsql("Host=localhost;Database=booking_acceptance_tests")
                .Options)
        {
            CallBase = true
        };

        public void VerifyNoAcceptanceNotification()
        {
            Notifications.Verify(
                service => service.SendBookingAcceptedAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()),
                Times.Never);
            Notifications.Verify(
                service => service.SendInAppNotificationAsync(
                    It.IsAny<Guid>(),
                    NotificationType.BookingAccepted,
                    It.IsAny<string>(),
                    It.IsAny<string>()),
                Times.Never);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
