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
    private readonly TimeProvider _timeProvider;

    public BookingTimeoutJob(
        IServiceScopeFactory scopeFactory,
        ILogger<BookingTimeoutJob> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

    internal async Task ProcessTimeoutsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var bookingRepository = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var cutoff = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-AppConstants.BookingTimeoutMinutes);
        var timedOut = await bookingRepository.GetTimedOutPendingBookingsAsync(cutoff);

        foreach (var candidate in timedOut)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Acceptance and timeout use the same ride lock. Only the operation that
            // actually changes Pending to Cancelled receives a booking to notify.
            var booking = await bookingRepository.CancelTimedOutAsync(
                candidate.Id,
                cutoff,
                cancellationToken);
            if (booking is null)
                continue;

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
