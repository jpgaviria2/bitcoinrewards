using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services
{
    /// <summary>
    /// Token bucket rate limiting service with in-memory storage
    /// TODO: Add Redis backing store for distributed deployments
    /// </summary>
    public class RateLimitService
    {
        private readonly ILogger<RateLimitService> _logger;
        private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new();
        private readonly SemaphoreSlim _cleanupLock = new(1, 1);
        private DateTime _lastCleanup = DateTime.UtcNow;
        private const int CLEANUP_INTERVAL_MINUTES = 5;
        
        public RateLimitService(ILogger<RateLimitService> logger)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// Check if a request is allowed under the rate limit policy
        /// </summary>
        public async Task<RateLimitState> CheckRateLimitAsync(
            string key,
            RateLimitPolicy policy,
            CancellationToken cancellationToken = default)
        {
            // Periodic cleanup of old buckets
            await CleanupOldBucketsAsync();
            
            var bucketKey = $"{policy.PolicyId}:{key}";
            var bucket = _buckets.GetOrAdd(bucketKey, _ => new TokenBucket
            {
                Capacity = policy.RequestsPerWindow + policy.BurstSize,
                RefillRate = policy.RequestsPerWindow / policy.WindowDuration.TotalSeconds,
                Tokens = policy.RequestsPerWindow + policy.BurstSize,
                LastRefill = DateTime.UtcNow
            });
            
            // Refill tokens based on elapsed time
            var now = DateTime.UtcNow;
            var elapsed = (now - bucket.LastRefill).TotalSeconds;
            var tokensToAdd = elapsed * bucket.RefillRate;
            
            lock (bucket)
            {
                bucket.Tokens = Math.Min(bucket.Capacity, bucket.Tokens + tokensToAdd);
                bucket.LastRefill = now;
                
                var state = new RateLimitState
                {
                    Key = key,
                    TokensRemaining = (int)Math.Floor(bucket.Tokens),
                    LastRefillTime = bucket.LastRefill,
                    ResetAt = new DateTimeOffset(bucket.LastRefill.Add(policy.WindowDuration)).ToUnixTimeSeconds(),
                    RequestCount = bucket.RequestCount,
                    IsLimited = bucket.Tokens < 1
                };
                
                if (bucket.Tokens >= 1)
                {
                    // Allow request and consume token
                    bucket.Tokens -= 1;
                    bucket.RequestCount++;
                    bucket.LastRequestTime = now;
                    state.TokensRemaining = (int)Math.Floor(bucket.Tokens);
                    return state;
                }
                
                // Rate limited
                _logger.LogWarning("Rate limit exceeded for key {Key} under policy {PolicyId}", 
                    key, policy.PolicyId);
                return state;
            }
        }
        
        /// <summary>
        /// Reset rate limit for a specific key (admin override)
        /// </summary>
        public void ResetRateLimit(string key, string policyId)
        {
            var bucketKey = $"{policyId}:{key}";
            _buckets.TryRemove(bucketKey, out _);
            _logger.LogInformation("Rate limit reset for key {Key} under policy {PolicyId}", key, policyId);
        }
        
        /// <summary>
        /// Get current rate limit state without consuming tokens
        /// </summary>
        public RateLimitState GetRateLimitState(string key, RateLimitPolicy policy)
        {
            var bucketKey = $"{policy.PolicyId}:{key}";
            if (!_buckets.TryGetValue(bucketKey, out var bucket))
            {
                return new RateLimitState
                {
                    Key = key,
                    TokensRemaining = policy.RequestsPerWindow + policy.BurstSize,
                    LastRefillTime = DateTime.UtcNow,
                    ResetAt = new DateTimeOffset(DateTime.UtcNow.Add(policy.WindowDuration)).ToUnixTimeSeconds(),
                    RequestCount = 0,
                    IsLimited = false
                };
            }
            
            lock (bucket)
            {
                return new RateLimitState
                {
                    Key = key,
                    TokensRemaining = (int)Math.Floor(bucket.Tokens),
                    LastRefillTime = bucket.LastRefill,
                    ResetAt = new DateTimeOffset(bucket.LastRefill.Add(policy.WindowDuration)).ToUnixTimeSeconds(),
                    RequestCount = bucket.RequestCount,
                    IsLimited = bucket.Tokens < 1
                };
            }
        }
        
        /// <summary>
        /// Clean up old token buckets to prevent memory leaks
        /// </summary>
        private async Task CleanupOldBucketsAsync()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastCleanup).TotalMinutes < CLEANUP_INTERVAL_MINUTES)
                return;
            
            if (!await _cleanupLock.WaitAsync(0))
                return; // Another cleanup in progress
            
            try
            {
                var cutoff = now.AddHours(-1); // Remove buckets inactive for >1 hour
                var removed = 0;
                
                foreach (var kvp in _buckets)
                {
                    if (kvp.Value.LastRequestTime < cutoff)
                    {
                        _buckets.TryRemove(kvp.Key, out _);
                        removed++;
                    }
                }
                
                _lastCleanup = now;
                
                if (removed > 0)
                {
                    _logger.LogDebug("Rate limit cleanup: removed {Count} inactive buckets", removed);
                }
            }
            finally
            {
                _cleanupLock.Release();
            }
        }
        
        /// <summary>
        /// Get statistics for monitoring
        /// </summary>
        public RateLimitStatistics GetStatistics()
        {
            var stats = new RateLimitStatistics
            {
                TotalBuckets = _buckets.Count,
                LimitedKeys = 0
            };
            
            foreach (var kvp in _buckets)
            {
                lock (kvp.Value)
                {
                    if (kvp.Value.Tokens < 1)
                    {
                        stats.LimitedKeys++;
                    }
                }
            }
            
            return stats;
        }
        
        /// <summary>
        /// Internal token bucket representation
        /// </summary>
        private class TokenBucket
        {
            public double Capacity { get; set; }
            public double RefillRate { get; set; }
            public double Tokens { get; set; }
            public DateTime LastRefill { get; set; }
            public DateTime LastRequestTime { get; set; }
            public int RequestCount { get; set; }
        }
    }
    
    /// <summary>
    /// Rate limit statistics for monitoring
    /// </summary>
    public class RateLimitStatistics
    {
        public int TotalBuckets { get; set; }
        public int LimitedKeys { get; set; }
    }
}
