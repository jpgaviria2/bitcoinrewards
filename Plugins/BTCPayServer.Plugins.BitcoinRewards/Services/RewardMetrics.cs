#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Plugins.BitcoinRewards.Exceptions;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Production metrics tracking for Bitcoin Rewards plugin.
/// Provides counters, gauges, and histograms for monitoring.
/// Compatible with Prometheus export format.
/// </summary>
public class RewardMetrics
{
    // Counters (monotonically increasing)
    private readonly ConcurrentDictionary<string, long> _counters = new();
    
    // Gauges (can go up or down)
    private readonly ConcurrentDictionary<string, long> _gauges = new();
    
    // Histograms (bucketed observations)
    private readonly ConcurrentDictionary<string, List<double>> _observations = new();
    
    // Labels for dimensional metrics
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _labels = new();
    
    /// <summary>
    /// Record a reward creation event (simple version)
    /// </summary>
    public void RecordRewardCreated(string platform, string storeId)
    {
        IncrementCounter($"rewards_created_total|platform={platform}|store={storeId}");
        IncrementCounter($"rewards_created_total|platform={platform}");
        IncrementCounter("rewards_created_total");
    }
    
    /// <summary>
    /// Record reward amount
    /// </summary>
    public void RecordRewardAmount(long amountSats)
    {
        RecordObservation("reward_amount_satoshis", (double)amountSats);
    }
    
    /// <summary>
    /// Record a reward creation event (with amount)
    /// </summary>
    public void RecordRewardCreated(decimal amountSats, string platform, string storeId)
    {
        RecordRewardCreated(platform, storeId);
        RecordRewardAmount((long)amountSats);
    }
    
    /// <summary>
    /// Record a reward claim event
    /// </summary>
    public void RecordRewardClaimed(string method, string storeId, decimal amountSats)
    {
        IncrementCounter($"rewards_claimed_total|method={method}|store={storeId}");
        IncrementCounter($"rewards_claimed_total|method={method}");
        IncrementCounter("rewards_claimed_total");
        
        RecordObservation($"reward_claim_amount_sats|method={method}", (double)amountSats);
    }
    
    /// <summary>
    /// Record an error event (simple)
    /// </summary>
    public void RecordError(string errorType, string storeId, string reason)
    {
        IncrementCounter($"errors_total|error_type={errorType}|store={storeId}");
        IncrementCounter($"errors_total|error_type={errorType}");
        IncrementCounter("errors_total");
    }
    
    /// <summary>
    /// Record an error event (with enum)
    /// </summary>
    public void RecordError(RewardErrorType errorType, string storeId)
    {
        var type = errorType.ToString();
        IncrementCounter($"reward_errors_total|type={type}|store={storeId}");
        IncrementCounter($"reward_errors_total|type={type}");
        IncrementCounter("reward_errors_total");
    }
    
    /// <summary>
    /// Record a Lightning Network operation
    /// </summary>
    public void RecordLightningOperation(string operation, string storeId, bool success)
    {
        IncrementCounter($"lightning_operations_total|operation={operation}|store={storeId}|success={success}");
    }
    
    /// <summary>
    /// Record webhook received
    /// </summary>
    public void RecordWebhookReceived(string platform, string storeId)
    {
        IncrementCounter($"webhooks_received_total|platform={platform}|store={storeId}");
    }
    
    /// <summary>
    /// Update the count of active (unclaimed) rewards
    /// </summary>
    public void UpdateActiveRewards(string storeId, int count)
    {
        SetGauge($"active_rewards|store={storeId}", count);
    }
    
    /// <summary>
    /// Update the total value of unclaimed rewards
    /// </summary>
    public void UpdateUnclaimedValue(string storeId, decimal totalSats)
    {
        SetGauge($"unclaimed_rewards_sats|store={storeId}", (long)totalSats);
    }
    
    /// <summary>
    /// Record operation duration (for performance tracking)
    /// </summary>
    public void RecordOperationDuration(string operation, double milliseconds)
    {
        RecordObservation($"operation_duration_ms|operation={operation}", milliseconds);
    }
    
    /// <summary>
    /// Record webhook processing time
    /// </summary>
    public void RecordWebhookDuration(string platform, string storeId, double milliseconds, bool success)
    {
        RecordObservation($"webhook_duration_ms|platform={platform}|store={storeId}", milliseconds);
        IncrementCounter($"webhooks_processed_total|platform={platform}|store={storeId}|success={success}");
    }
    
    // Helper methods
    
    private void IncrementCounter(string key, long amount = 1)
    {
        _counters.AddOrUpdate(key, amount, (k, v) => v + amount);
    }
    
    private void SetGauge(string key, long value)
    {
        _gauges.AddOrUpdate(key, value, (k, v) => value);
    }
    
    private void RecordObservation(string key, double value)
    {
        _observations.AddOrUpdate(
            key,
            new List<double> { value },
            (k, list) =>
            {
                lock (list)
                {
                    list.Add(value);
                    // Keep only last 1000 observations per metric to prevent memory issues
                    if (list.Count > 1000)
                    {
                        list.RemoveRange(0, list.Count - 1000);
                    }
                    return list;
                }
            });
    }
    
    /// <summary>
    /// Get current counter value
    /// </summary>
    public long GetCounter(string name)
    {
        var match = _counters.Where(kv => kv.Key.StartsWith(name)).Sum(kv => kv.Value);
        return match;
    }
    
    /// <summary>
    /// Get current gauge value
    /// </summary>
    public long GetGauge(string name)
    {
        var match = _gauges.Where(kv => kv.Key.StartsWith(name)).Sum(kv => kv.Value);
        return match;
    }
    
    /// <summary>
    /// Get histogram statistics
    /// </summary>
    public HistogramStats? GetHistogramStats(string name)
    {
        var observations = _observations
            .Where(kv => kv.Key.StartsWith(name))
            .SelectMany(kv => kv.Value)
            .ToList();
        
        if (!observations.Any())
            return null;
        
        observations.Sort();
        
        return new HistogramStats
        {
            Count = observations.Count,
            Sum = observations.Sum(),
            Min = observations.First(),
            Max = observations.Last(),
            Mean = observations.Average(),
            P50 = GetPercentile(observations, 0.50),
            P95 = GetPercentile(observations, 0.95),
            P99 = GetPercentile(observations, 0.99)
        };
    }
    
    private static double GetPercentile(List<double> sortedValues, double percentile)
    {
        if (!sortedValues.Any())
            return 0;
        
        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        index = Math.Max(0, Math.Min(sortedValues.Count - 1, index));
        return sortedValues[index];
    }
    
    /// <summary>
    /// Export metrics in Prometheus text format
    /// </summary>
    public string ExportPrometheusFormat()
    {
        var lines = new List<string>();
        
        // Export counters
        foreach (var (key, value) in _counters.OrderBy(kv => kv.Key))
        {
            var (name, labels) = ParseKey(key);
            lines.Add($"{name}{{{labels}}} {value}");
        }
        
        // Export gauges
        foreach (var (key, value) in _gauges.OrderBy(kv => kv.Key))
        {
            var (name, labels) = ParseKey(key);
            lines.Add($"{name}{{{labels}}} {value}");
        }
        
        // Export histograms (simplified - just show stats as gauges)
        foreach (var (key, _) in _observations.OrderBy(kv => kv.Key))
        {
            var stats = GetHistogramStats(key);
            if (stats != null)
            {
                var (name, labels) = ParseKey(key);
                var labelPrefix = string.IsNullOrEmpty(labels) ? "" : $",{labels}";
                
                lines.Add($"{name}_count{{{labelPrefix}}} {stats.Count}");
                lines.Add($"{name}_sum{{{labelPrefix}}} {stats.Sum}");
                lines.Add($"{name}_mean{{{labelPrefix}}} {stats.Mean}");
                lines.Add($"{name}_p95{{{labelPrefix}}} {stats.P95}");
            }
        }
        
        return string.Join("\n", lines);
    }
    
    private static (string name, string labels) ParseKey(string key)
    {
        var parts = key.Split('|');
        var name = parts[0];
        var labels = parts.Length > 1 ? string.Join(",", parts.Skip(1)) : "";
        return (name, labels);
    }
    
    /// <summary>
    /// Get a summary of all metrics
    /// </summary>
    public MetricsSummary GetSummary()
    {
        return new MetricsSummary
        {
            Counters = _counters.ToDictionary(kv => kv.Key, kv => kv.Value),
            Gauges = _gauges.ToDictionary(kv => kv.Key, kv => kv.Value),
            Histograms = _observations.Keys
                .Select(key => new { Key = key, Stats = GetHistogramStats(key) })
                .Where(x => x.Stats != null)
                .ToDictionary(x => x.Key, x => x.Stats!)
        };
    }
    
    /// <summary>
    /// Get snapshot of all metrics (alias for GetSummary for backward compatibility)
    /// </summary>
    public MetricsSummary GetSnapshot() => GetSummary();

}
/// <summary>
/// Histogram statistics
/// </summary>
public class HistogramStats
{
    public int Count { get; set; }
    public double Sum { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double Mean { get; set; }
    public double P50 { get; set; }
    public double P95 { get; set; }
    public double P99 { get; set; }
}

/// <summary>
/// Complete metrics summary
/// </summary>
public class MetricsSummary
{
    public Dictionary<string, long> Counters { get; set; } = new();
    public Dictionary<string, long> Gauges { get; set; } = new();
    public Dictionary<string, HistogramStats> Histograms { get; set; } = new();
}
