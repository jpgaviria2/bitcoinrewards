using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.HostedServices;

/// <summary>
/// Background service for periodic maintenance tasks:
/// - Cleanup expired idempotency cache entries
/// - Cleanup expired LNURL claims
/// - Cleanup old transaction history (optional)
/// </summary>
public class MaintenanceService : BackgroundService
{
    private readonly ILogger<MaintenanceService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

    public MaintenanceService(ILogger<MaintenanceService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MaintenanceService started - cleanup runs every {Interval}", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, stoppingToken);
                await PerformMaintenanceAsync();
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Maintenance task failed, will retry in {Interval}", _interval);
            }
        }

        _logger.LogInformation("MaintenanceService stopped");
    }

    private async Task PerformMaintenanceAsync()
    {
        _logger.LogInformation("Starting maintenance tasks");
        var startTime = DateTime.UtcNow;

        try
        {
            // Cleanup idempotency cache
            var removedIdempotency = Services.IdempotencyService.CleanupExpiredEntries();
            _logger.LogInformation("Cleaned up {Count} expired idempotency entries", removedIdempotency);

            // Cleanup rate limit histories (if middleware is active)
            try
            {
                Middleware.RateLimitMiddleware.CleanupExpiredHistories();
                _logger.LogInformation("Cleaned up expired rate limit histories");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Rate limit cleanup failed (middleware may not be active)");
            }

            // TODO: Cleanup old PendingLnurlClaims (completed > 7 days ago)
            // TODO: Archive old WalletTransactions (> 1 year old)

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Maintenance completed in {Duration}ms", duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Maintenance tasks failed");
        }
    }
}
