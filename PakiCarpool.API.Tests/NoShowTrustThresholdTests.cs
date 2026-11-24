using Microsoft.EntityFrameworkCore;
using Moq;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Repository.EFCore;
using PakiCarpool.Repository.EFCore.Repositories;
using PakiCarpool.Services.Implementations;
using Xunit;

namespace PakiCarpool.API.Tests;

/// <summary>
/// Threshold behavior for no-shows (backlog item 10):
/// 2 no-shows → automatic in-app + email warning; 4 no-shows → an open
/// admin-review flag raised exactly once — the account is NOT auto-suspended,
/// because the suspension decision belongs to an admin.
/// </summary>
public sealed class NoShowTrustThresholdTests
{
    [Fact]
    public async Task SecondNoShow_SendsWarningWithoutFlagOrSuspension()
    {
        using var fixture = new ThresholdFixture(noShowCount: 2);

        await fixture.TrustService.CheckThresholdsAsync(fixture.OffenderId);

        fixture.Notifications.Verify(service => service.SendInAppNotificationAsync(
            fixture.OffenderId, NotificationType.WarningIssued,
            "No-Show Warning", It.IsAny<string>()), Times.Once);
        fixture.Email.Verify(service => service.SendWarningEmailAsync(
            fixture.Offender.Email, fixture.Offender.FullName, It.IsAny<string>()), Times.Once);
        Assert.Empty(fixture.Context.Flags.ToList());
        var user = await fixture.Context.Users.AsNoTracking().SingleAsync(u => u.Id == fixture.OffenderId);
        Assert.False(user.IsSuspended);
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task FourthNoShow_CreatesAdminFlag_DoesNotSuspend()
    {
        using var fixture = new ThresholdFixture(noShowCount: 4);

        await fixture.TrustService.CheckThresholdsAsync(fixture.OffenderId);

        var flag = Assert.Single(fixture.Context.Flags.ToList());
        Assert.Equal(fixture.LatestReporterId, flag.ReporterId);
        Assert.Equal(fixture.OffenderId, flag.ReporteeId);
        Assert.Equal(fixture.RideId, flag.RideId);
        Assert.Equal(AppConstants.NoShowReviewFlagReason, flag.Reason);
        Assert.Equal(FlagStatus.Open, flag.Status);
        var user = await fixture.Context.Users.AsNoTracking().SingleAsync(u => u.Id == fixture.OffenderId);
        Assert.False(user.IsSuspended);
        Assert.True(user.IsActive);
        fixture.Notifications.Verify(service => service.SendInAppNotificationAsync(
            It.IsAny<Guid>(), NotificationType.AccountSuspended, It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        fixture.Email.Verify(service => service.SendSuspensionEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RepeatedThresholdChecks_DoNotCreateDuplicateFlags()
    {
        using var fixture = new ThresholdFixture(noShowCount: 4);

        await fixture.TrustService.CheckThresholdsAsync(fixture.OffenderId);
        await fixture.TrustService.CheckThresholdsAsync(fixture.OffenderId);
        await fixture.TrustService.CheckThresholdsAsync(fixture.OffenderId);

        Assert.Single(fixture.Context.Flags.ToList());
    }

    private sealed class ThresholdFixture : IDisposable
    {
        public ThresholdFixture(int noShowCount)
        {
            Context = new PakiCarpoolDbContext(
                new DbContextOptionsBuilder<PakiCarpoolDbContext>()
                    .UseInMemoryDatabase($"no-show-threshold-{Guid.NewGuid():N}")
                    .Options);

            OffenderId = Guid.NewGuid();
            LatestReporterId = Guid.NewGuid();
            RideId = Guid.NewGuid();
            Offender = new User
            {
                Id = OffenderId,
                FullName = "Threshold Offender",
                Email = "threshold-offender@example.com",
                Role = UserRole.Passenger,
                TrustScore = AppConstants.TrustScoreInitial,
                IsActive = true
            };
            Context.Add(Offender);
            for (var index = 0; index < noShowCount; index++)
            {
                // The most recent log carries LatestReporterId so flag attribution is deterministic.
                Context.Add(new NoShowLog
                {
                    Id = Guid.NewGuid(),
                    ReporterId = index == noShowCount - 1 ? LatestReporterId : Guid.NewGuid(),
                    OffenderId = OffenderId,
                    RideId = RideId,
                    BookingId = Guid.NewGuid(),
                    CreatedAt = DateTime.UtcNow.AddMinutes(-(noShowCount - index))
                });
            }
            Context.SaveChanges();

            TrustService = new TrustService(
                new UserRepository(Context),
                new AdminRepository(Context),
                Notifications.Object,
                Email.Object);
        }

        public Guid OffenderId { get; }
        public Guid LatestReporterId { get; }
        public Guid RideId { get; }
        public User Offender { get; }
        public PakiCarpoolDbContext Context { get; }
        public TrustService TrustService { get; }
        public Mock<INotificationService> Notifications { get; } = new();
        public Mock<IEmailService> Email { get; } = new();

        public void Dispose() => Context.Dispose();
    }
}
