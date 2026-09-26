using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PakiCarpool.Interfaces.IRepositories;

namespace PakiCarpool.Services.BackgroundJobs;

/// <summary>
/// Deletes revoked or expired refresh tokens daily at 02:00 UTC.
/// </summary>
public class RefreshTokenCleanupJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RefreshTokenCleanupJob> _logger;

    public RefreshTokenCleanupJob(
        IServiceScopeFactory scopeFactory,
        ILogger<RefreshTokenCleanupJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextRun(hourUtc: 2);
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
                var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                await userRepository.CleanupRefreshTokensAsync();
                _logger.LogInformation("RefreshTokenCleanupJob completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RefreshTokenCleanupJob failed.");
            }
        }
    }

    private static TimeSpan GetDelayUntilNextRun(int hourUtc)
    {
        var now = DateTime.UtcNow;
        var next = new DateTime(now.Year, now.Month, now.Day, hourUtc, 0, 0, DateTimeKind.Utc);
        if (next <= now)
            next = next.AddDays(1);
        return next - now;
    }
}
