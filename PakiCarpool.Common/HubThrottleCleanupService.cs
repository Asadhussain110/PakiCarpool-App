using Microsoft.Extensions.Hosting;

namespace PakiCarpool.Common;

/// <summary>
/// Periodically removes throttle buckets whose windows have expired beyond the cleanup grace period.
/// </summary>
public sealed class HubThrottleCleanupService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                ThrottledHub.SweepExpiredBuckets(DateTime.UtcNow);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}