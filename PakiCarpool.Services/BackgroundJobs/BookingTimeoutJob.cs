using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.Services.BackgroundJobs;

/// <summary>
/// Cancels Pending bookings whose 2-minute acceptance window has expired.
/// Runs every 30 seconds. Sends BookingAutoCancelled to the passenger via SignalR.
/// </summary>
public class BookingTimeoutJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookingTimeoutJob> _logger;

    public BookingTimeoutJob(IServiceScopeFactory scopeFactory, ILogger<BookingTimeoutJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessTimeoutsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BookingTimeoutJob failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task ProcessTimeoutsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var bookingRepository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var cutoff = DateTime.UtcNow.AddMinutes(-AppConstants.BookingTimeoutMinutes);
        var timedOut = await bookingRepository.GetTimedOutPendingBookingsAsync(cutoff);

        foreach (var booking in timedOut)
        {
            cancellationToken.ThrowIfCancellationRequested();

            booking.Status = BookingStatus.Cancelled;
            booking.CancelReason = "Auto-cancelled: driver did not respond within 2 minutes";
            await bookingRepository.UpdateAsync(booking);

            // Named event — Flutter client receives structured bookingId payload
            await notificationService.SendBookingAutoCancelledAsync(booking.PassengerId, booking.Id);

            // Generic in-app + push via the notification matrix
            await notificationService.SendNotificationAsync(
                booking.PassengerId,
                NotificationType.BookingAutoCancelled,
                "Booking Timed Out",
                "Your booking request expired because the driver did not respond in time.");

            _logger.LogInformation("Auto-cancelled booking {BookingId}", booking.Id);
        }
    }
}
