using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.BitcoinRewards.Models
{
    /// <summary>
    /// Rate limiting policy configuration
    /// </summary>
    public class RateLimitPolicy
    {
        /// <summary>
        /// Policy identifier (e.g., "webhook", "api", "admin")
        /// </summary>
        public string PolicyId { get; set; } = string.Empty;
        
        /// <summary>
        /// Maximum requests per window
        /// </summary>
        public int RequestsPerWindow { get; set; } = 60;
        
        /// <summary>
        /// Time window duration
        /// </summary>
        public TimeSpan WindowDuration { get; set; } = TimeSpan.FromMinutes(1);
        
        /// <summary>
        /// Burst capacity (allows temporary spikes above limit)
        /// </summary>
        public int BurstSize { get; set; } = 10;
        
        /// <summary>
        /// Whether to return detailed rate limit headers
        /// </summary>
        public bool IncludeHeaders { get; set; } = true;
    }
    
    /// <summary>
    /// Rate limit state for a specific key (IP, store, etc.)
    /// </summary>
    public class RateLimitState
    {
        /// <summary>
        /// Unique identifier (IP address, store ID, etc.)
        /// </summary>
        public string Key { get; set; } = string.Empty;
        
        /// <summary>
        /// Number of tokens remaining in bucket
        /// </summary>
        public int TokensRemaining { get; set; }
        
        /// <summary>
        /// When the bucket was last refilled
        /// </summary>
        public DateTime LastRefillTime { get; set; }
        
        /// <summary>
        /// Window reset timestamp (Unix epoch)
        /// </summary>
        public long ResetAt { get; set; }
        
        /// <summary>
        /// Total requests in current window
        /// </summary>
        public int RequestCount { get; set; }
        
        /// <summary>
        /// Whether the limit is currently exceeded
        /// </summary>
        public bool IsLimited { get; set; }
    }
    
    /// <summary>
    /// Rate limit configuration stored in settings
    /// </summary>
    public class RateLimitConfiguration
    {
        /// <summary>
        /// Per-IP rate limits for webhooks
        /// </summary>
        public RateLimitPolicy WebhookPolicy { get; set; } = new()
        {
            PolicyId = "webhook",
            RequestsPerWindow = 60,
            WindowDuration = TimeSpan.FromMinutes(1),
            BurstSize = 10
        };
        
        /// <summary>
        /// Per-IP rate limits for API endpoints
        /// </summary>
        public RateLimitPolicy ApiPolicy { get; set; } = new()
        {
            PolicyId = "api",
            RequestsPerWindow = 120,
            WindowDuration = TimeSpan.FromMinutes(1),
            BurstSize = 20
        };
        
        /// <summary>
        /// Per-IP rate limits for admin UI
        /// </summary>
        public RateLimitPolicy AdminPolicy { get; set; } = new()
        {
            PolicyId = "admin",
            RequestsPerWindow = 300,
            WindowDuration = TimeSpan.FromMinutes(1),
            BurstSize = 50
        };
        
        /// <summary>
        /// Per-store rate limits (applies to all requests for that store)
        /// </summary>
        public RateLimitPolicy StorePolicy { get; set; } = new()
        {
            PolicyId = "store",
            RequestsPerWindow = 100,
            WindowDuration = TimeSpan.FromMinutes(1),
            BurstSize = 20
        };
        
        /// <summary>
        /// Whitelisted IP addresses (no rate limits)
        /// </summary>
        public List<string> WhitelistedIps { get; set; } = new();
        
        /// <summary>
        /// Blacklisted IP addresses (always blocked)
        /// </summary>
        public List<string> BlacklistedIps { get; set; } = new();
        
        /// <summary>
        /// Whether rate limiting is enabled globally
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}
