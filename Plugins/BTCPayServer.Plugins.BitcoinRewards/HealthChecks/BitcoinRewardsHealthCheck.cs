#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.HealthChecks;

public class BitcoinRewardsHealthCheck : IHealthCheck
{
    private readonly BitcoinRewardsPluginDbContextFactory _dbFactory;
    private readonly ILogger<BitcoinRewardsHealthCheck> _logger;
    
    public BitcoinRewardsHealthCheck(
        BitcoinRewardsPluginDbContextFactory dbFactory,
        ILogger<BitcoinRewardsHealthCheck> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var checks = new Dictionary<string, object>();
        var warnings = new List<string>();
        
        try
        {
            // 1. Database connectivity check
            await using var db = _dbFactory.CreateContext();
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            
            if (!canConnect)
            {
                return HealthCheckResult.Unhealthy("Database connection failed", null, checks);
            }
            
            checks["database"] = "connected";
            
            // 2. Recent reward success rate (last 24 hours)
            try
            {
                var last24h = DateTime.UtcNow.AddHours(-24);
                var recentRewards = await db.BitcoinRewardRecords
                    .Where(r => r.CreatedAt >= last24h)
                    .ToListAsync(cancellationToken);
                
                if (recentRewards.Any())
                {
                    var claimed = recentRewards.Count(r => r.ClaimedAt != null);
                    var total = recentRewards.Count;
                    var successRate = claimed / (double)total;
                    
                    checks["rewards_24h_total"] = total;
                    checks["rewards_24h_claimed"] = claimed;
                    checks["rewards_24h_success_rate"] = $"{successRate:P1}";
                    
                    if (successRate < 0.50) // Less than 50% claimed is concerning
                    {
                        warnings.Add($"Low claim rate in last 24h: {successRate:P}");
                    }
                }
                else
                {
                    checks["rewards_24h_total"] = 0;
                    checks["rewards_24h_note"] = "No rewards in last 24 hours";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to calculate reward success rate");
                warnings.Add("Could not calculate success rate");
            }
            
            // 3. Check for stuck/old unclaimed rewards
            try
            {
                var threeDaysAgo = DateTime.UtcNow.AddDays(-3);
                var stuckRewards = await db.BitcoinRewardRecords
                    .Where(r => r.ClaimedAt == null && r.CreatedAt < threeDaysAgo)
                    .CountAsync(cancellationToken);
                
                checks["stuck_rewards_count"] = stuckRewards;
                
                if (stuckRewards > 10)
                {
                    warnings.Add($"{stuckRewards} unclaimed rewards older than 3 days");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to check for stuck rewards");
            }
            
            // 4. Database size/health metrics
            try
            {
                var totalRewards = await db.BitcoinRewardRecords.CountAsync(cancellationToken);
                checks["total_rewards_all_time"] = totalRewards;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to get total reward count");
            }
            
            // Return result
            if (warnings.Any())
            {
                return HealthCheckResult.Degraded(
                    $"System degraded: {string.Join("; ", warnings)}", 
                    null, 
                    checks);
            }
            
            return HealthCheckResult.Healthy("Bitcoin Rewards system operational", checks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed with exception");
            return HealthCheckResult.Unhealthy(
                "Health check failed", 
                ex, 
                checks);
        }
    }
}
