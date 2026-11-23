using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PakiCarpool.Interfaces.IRepositories;

namespace PakiCarpool.Services.BackgroundJobs;

/// <summary>
/// Deletes stale <c>LivenessChallenges</c> rows daily at 04:00 UTC.
/// A row is stale when <c>ExpiresAt &lt; NOW()</c> OR <c>IsUsed = true</c>.
/// </summary>
public class LivenessChallengeCleanupJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LivenessChallengeCleanupJob> _logger;

    public LivenessChallengeCleanupJob(
        IServiceScopeFactory scopeFactory,
        ILogger<LivenessChallengeCleanupJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextRun(hourUtc: 4);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<ILivenessChallengeRepository>();
                await repo.CleanupChallengesAsync();
                _logger.LogInformation("LivenessChallengeCleanupJob completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LivenessChallengeCleanupJob failed.");
            }
        }
    }

    private static TimeSpan GetDelayUntilNextRun(int hourUtc)
    {
        var now  = DateTime.UtcNow;
        var next = new DateTime(now.Year, now.Month, now.Day, hourUtc, 0, 0, DateTimeKind.Utc);
        if (next <= now)
            next = next.AddDays(1);
        return next - now;
    }
}
