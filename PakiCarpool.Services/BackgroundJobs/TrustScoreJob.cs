using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;

namespace PakiCarpool.Services.BackgroundJobs;

/// <summary>
/// Recalculates driver AverageRating from the Ratings table daily at 03:00 UTC.
/// Also recalculates trust-adjacent averages for passengers where applicable.
/// </summary>
public class TrustScoreJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrustScoreJob> _logger;

    public TrustScoreJob(IServiceScopeFactory scopeFactory, ILogger<TrustScoreJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextRun(hourUtc: 3);
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
                await RecalculateAsync(stoppingToken);
                _logger.LogInformation("TrustScoreJob completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TrustScoreJob failed.");
            }
        }
    }

    private async Task RecalculateAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var trustService = scope.ServiceProvider.GetRequiredService<ITrustService>();

        var page = 1;
        const int pageSize = 100;

        while (!cancellationToken.IsCancellationRequested)
        {
            var (users, totalCount) = await userRepository.GetAllPaginatedAsync(page, pageSize);
            if (users.Count == 0)
                break;

            foreach (var user in users.Where(u => u.Role == UserRole.Driver))
            {
                var profile = await userRepository.GetDriverProfileByUserIdAsync(user.Id);
                if (profile is null) continue;
                await trustService.RecalculateDriverRatingAsync(profile.Id);
            }

            if (page * pageSize >= totalCount)
                break;

            page++;
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
