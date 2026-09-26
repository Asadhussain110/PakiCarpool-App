using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PakiCarpool.Interfaces.IRepositories;

namespace PakiCarpool.Services.BackgroundJobs;

/// <summary>
/// Deletes ChatMessages where ExpiresAt &lt; now. Runs every hour.
/// Enforces the 7-day chat retention policy.
/// </summary>
public class ChatExpiryJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChatExpiryJob> _logger;

    public ChatExpiryJob(IServiceScopeFactory scopeFactory, ILogger<ChatExpiryJob> logger)
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
                var chatRepository = scope.ServiceProvider.GetRequiredService<IChatRepository>();
                await chatRepository.DeleteExpiredMessagesAsync();
                _logger.LogInformation("ChatExpiryJob purged expired messages.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChatExpiryJob failed.");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
