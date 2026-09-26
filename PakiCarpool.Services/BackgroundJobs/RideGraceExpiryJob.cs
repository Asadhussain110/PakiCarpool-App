using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PakiCarpool.Interfaces.IRepositories;

namespace PakiCarpool.Services.BackgroundJobs;

/// <summary>
/// Finds Posted rides whose CancelGraceExpiresAt has passed.
/// Runs every 30 seconds. Used for penalty enforcement on late cancellations —
/// no auto-cancel; grace expiry is evaluated at cancel time against CancelGraceExpiresAt.
/// This job logs and surfaces expired-grace rides for operational visibility.
/// </summary>
public class RideGraceExpiryJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RideGraceExpiryJob> _logger;

    public RideGraceExpiryJob(
        IServiceScopeFactory scopeFactory,
        ILogger<RideGraceExpiryJob> logger)
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
                using var scope = _scopeFactory.CreateScope();
                var rideRepository = scope.ServiceProvider.GetRequiredService<IRideRepository>();
                var expired = await rideRepository.GetExpiredGracePeriodRidesAsync();

                if (expired.Count > 0)
                {
                    _logger.LogInformation(
                        "RideGraceExpiryJob: {Count} Posted ride(s) past cancel grace period (penalty applies on cancel).",
                        expired.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RideGraceExpiryJob failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
