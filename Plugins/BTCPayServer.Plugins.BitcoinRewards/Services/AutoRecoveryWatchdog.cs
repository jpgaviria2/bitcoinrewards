using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services
{
    /// <summary>
    /// Background service that automatically retries failed operations
    /// with exponential backoff strategy
    /// </summary>
    public class AutoRecoveryWatchdog : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<AutoRecoveryWatchdog> _logger;
        private readonly RewardMetrics _metrics;
        
        // Retry policy constants
        private const int MAX_RETRY_ATTEMPTS = 5;
        private const int CHECK_INTERVAL_MINUTES = 5;
        private static readonly TimeSpan[] RETRY_DELAYS = new[]
        {
            TimeSpan.Zero,              // Attempt 1: Immediate
            TimeSpan.FromMinutes(5),    // Attempt 2: 5 min
            TimeSpan.FromMinutes(15),   // Attempt 3: 15 min
            TimeSpan.FromMinutes(45),   // Attempt 4: 45 min
            TimeSpan.FromHours(2.25)    // Attempt 5: 2.25 hours
        };
        
        public AutoRecoveryWatchdog(
            IServiceProvider services,
            ILogger<AutoRecoveryWatchdog> logger,
            RewardMetrics metrics)
        {
            _services = services;
            _logger = logger;
            _metrics = metrics;
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Auto-Recovery Watchdog started");
            
            // Wait 1 minute before first run to let system stabilize
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var errorTracking = scope.ServiceProvider.GetRequiredService<ErrorTrackingService>();
                    var rewardsService = scope.ServiceProvider.GetRequiredService<BitcoinRewardsService>();
                    var repository = scope.ServiceProvider.GetRequiredService<BitcoinRewardsRepository>();
                    var storeRepository = scope.ServiceProvider.GetRequiredService<StoreRepository>();
                    
                    // 1. Recover orphaned rewards (rewards with no pull payment)
                    await RecoverOrphanedRewardsAsync(rewardsService, repository, stoppingToken);
                    
                    // 2. Retry retryable errors
                    await RetryFailedOperationsAsync(errorTracking, rewardsService, repository, storeRepository, stoppingToken);
                    
                    // 3. Escalate errors that exceeded max retries
                    await EscalateMaxRetriesAsync(errorTracking, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Auto-Recovery Watchdog execution");
                }
                
                // Wait before next check
                await Task.Delay(TimeSpan.FromMinutes(CHECK_INTERVAL_MINUTES), stoppingToken);
            }
            
            _logger.LogInformation("Auto-Recovery Watchdog stopped");
        }
        
        /// <summary>
        /// Recover rewards that were created in DB but missing pull payment
        /// </summary>
        private async Task RecoverOrphanedRewardsAsync(
            BitcoinRewardsService rewardsService,
            BitcoinRewardsRepository repository,
            CancellationToken cancellationToken)
        {
            try
            {
                // Get all stores with rewards
                var stores = await repository.GetStoresWithRewardsAsync();
                var totalRecovered = 0;
                
                foreach (var storeId in stores)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    
                    try
                    {
                        var recovered = await rewardsService.RecoverOrphanedRewardsAsync(storeId);
                        if (recovered > 0)
                        {
                            totalRecovered += recovered;
                            _logger.LogInformation("Recovered {Count} orphaned rewards for store {StoreId}", 
                                recovered, storeId);
                            _metrics.RecordLightningOperation("auto_recovery_orphans", storeId, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error recovering orphaned rewards for store {StoreId}", storeId);
                        _metrics.RecordLightningOperation("auto_recovery_orphans", storeId, false);
                    }
                }
                
                if (totalRecovered > 0)
                {
                    _logger.LogInformation("Total orphaned rewards recovered: {Count}", totalRecovered);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in orphaned rewards recovery");
            }
        }
        
        /// <summary>
        /// Retry failed operations based on error retryability
        /// </summary>
        private async Task RetryFailedOperationsAsync(
            ErrorTrackingService errorTracking,
            BitcoinRewardsService rewardsService,
            BitcoinRewardsRepository repository,
            StoreRepository storeRepository,
            CancellationToken cancellationToken)
        {
            try
            {
                // Get all stores
                var stores = await repository.GetStoresWithRewardsAsync();
                
                foreach (var storeId in stores)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    
                    // Get retryable errors for this store
                    var retryableErrors = await errorTracking.GetRetryableErrorsAsync(maxRetries: 3, limit: 20);
                    
                    foreach (var error in retryableErrors)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;
                        
                        // Check if enough time has passed since last retry
                        var retryDelay = GetRetryDelay(error.RetryCount);
                        var timeSinceLastAttempt = DateTime.UtcNow - (error.LastRetryAt ?? error.Timestamp);
                        
                        if (timeSinceLastAttempt < retryDelay)
                        {
                            // Not yet time to retry
                            continue;
                        }
                        
                        // Check if max retries exceeded
                        if (error.RetryCount >= MAX_RETRY_ATTEMPTS)
                        {
                            // Will be escalated in next step
                            continue;
                        }
                        
                        // Attempt retry
                        await RetryErrorAsync(error, rewardsService, repository, storeRepository, errorTracking);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in retry failed operations");
            }
        }
        
        /// <summary>
        /// Retry a specific error
        /// </summary>
        private async Task RetryErrorAsync(
            RewardError error,
            BitcoinRewardsService rewardsService,
            BitcoinRewardsRepository repository,
            StoreRepository storeRepository,
            ErrorTrackingService errorTracking)
        {
            _logger.LogInformation("Retrying error {ErrorId} (attempt {Attempt}/{Max}): {Operation}",
                error.Id, error.RetryCount + 1, MAX_RETRY_ATTEMPTS, error.Operation);
            
            await errorTracking.RecordRetryAttemptAsync(error.Id);
            
            try
            {
                var success = error.Operation switch
                {
                    "ProcessRewardAsync" => await RetryProcessRewardAsync(error, rewardsService, repository),
                    _ => false
                };
                
                if (success)
                {
                    await errorTracking.ResolveErrorAsync(error.Id, "AutoRecoveryWatchdog");
                    _logger.LogInformation("Successfully retried error {ErrorId}: {Operation}", 
                        error.Id, error.Operation);
                }
                else
                {
                    _logger.LogWarning("Retry failed for error {ErrorId}: {Operation}", 
                        error.Id, error.Operation);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during retry of error {ErrorId}", error.Id);
            }
        }
        
        /// <summary>
        /// Retry processing a reward
        /// </summary>
        private async Task<bool> RetryProcessRewardAsync(
            RewardError error,
            BitcoinRewardsService rewardsService,
            BitcoinRewardsRepository repository)
        {
            if (string.IsNullOrEmpty(error.RewardId))
                return false;
            
            var reward = await repository.GetRewardByIdAsync(error.RewardId);
            if (reward == null)
            {
                _logger.LogWarning("Reward {RewardId} not found for retry", error.RewardId);
                return false;
            }
            
            // Attempt to recover orphaned reward (re-create pull payment)
            var recovered = await rewardsService.RecoverOrphanedRewardsAsync(reward.StoreId);
            return recovered > 0;
        }
        
        /// <summary>
        /// Escalate errors that exceeded max retry attempts
        /// </summary>
        private async Task EscalateMaxRetriesAsync(
            ErrorTrackingService errorTracking,
            CancellationToken cancellationToken)
        {
            try
            {
                // This would integrate with BTCPay's notification system
                // For now, just log at ERROR level
                
                var recentErrors = await errorTracking.GetRecentErrorsAsync(limit: 100, filterType: null, storeId: null, resolvedOnly: false);
                var exceededErrors = recentErrors
                    .Where(e => e.IsRetryable && e.RetryCount >= MAX_RETRY_ATTEMPTS && !e.IsResolved)
                    .ToList();
                
                if (exceededErrors.Any())
                {
                    _logger.LogError("⚠️ ESCALATION: {Count} errors exceeded max retry attempts and require manual intervention",
                        exceededErrors.Count);
                    
                    foreach (var error in exceededErrors)
                    {
                        _logger.LogError("Escalated error {ErrorId}: {Operation} - {Message}",
                            error.Id, error.Operation, error.ErrorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in escalation check");
            }
        }
        
        /// <summary>
        /// Get retry delay based on attempt count (exponential backoff)
        /// </summary>
        private static TimeSpan GetRetryDelay(int retryCount)
        {
            if (retryCount < 0 || retryCount >= RETRY_DELAYS.Length)
                return RETRY_DELAYS[^1]; // Return last (max) delay
            
            return RETRY_DELAYS[retryCount];
        }
    }
}
