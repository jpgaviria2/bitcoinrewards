#nullable enable

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Middleware;

/// <summary>
/// Rate limiting middleware to protect Bitcoin Rewards endpoints from abuse
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    
    // Stores: key -> (timestamp, count)
    private static readonly ConcurrentDictionary<string, RateLimitEntry> _rateLimits = new();
    
    // Cleanup old entries periodically
    private static DateTime _lastCleanup = DateTime.UtcNow;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);
    
    public RateLimitingMiddleware(
        RequestDelegate next,
        ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        
        // Check if this path requires rate limiting
        var limit = GetRateLimitForPath(path);
        
        if (limit != null)
        {
            var clientId = GetClientIdentifier(context);
            var key = $"{path}:{clientId}";
            
            // Check rate limit
            if (!TryAcquire(key, limit.MaxRequests, limit.WindowSeconds))
            {
                context.Response.StatusCode = 429; // Too Many Requests
                context.Response.Headers["Retry-After"] = limit.WindowSeconds.ToString();
                context.Response.Headers["X-RateLimit-Limit"] = limit.MaxRequests.ToString();
                context.Response.Headers["X-RateLimit-Window"] = $"{limit.WindowSeconds}s";
                
                _logger.LogWarning(
                    "Rate limit exceeded for {Path} by {ClientId}. Limit: {MaxRequests}/{WindowSeconds}s",
                    path, clientId, limit.MaxRequests, limit.WindowSeconds);
                
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Rate limit exceeded",
                    message = $"Too many requests. Limit: {limit.MaxRequests} requests per {limit.WindowSeconds} seconds.",
                    retry_after = limit.WindowSeconds
                });
                
                return;
            }
        }
        
        // Periodic cleanup of old entries
        MaybeCleanup();
        
        await _next(context);
    }
    
    private bool TryAcquire(string key, int maxRequests, int windowSeconds)
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddSeconds(-windowSeconds);
        
        var entry = _rateLimits.AddOrUpdate(
            key,
            // Add new entry
            k => new RateLimitEntry 
            { 
                WindowStart = now, 
                Count = 1 
            },
            // Update existing entry
            (k, existing) =>
            {
                // If window has expired, reset
                if (existing.WindowStart < windowStart)
                {
                    return new RateLimitEntry 
                    { 
                        WindowStart = now, 
                        Count = 1 
                    };
                }
                
                // Increment count in current window
                existing.Count++;
                return existing;
            });
        
        return entry.Count <= maxRequests;
    }
    
    private RateLimitConfig? GetRateLimitForPath(string path)
    {
        // Webhook endpoints - stricter limits
        if (path.Contains("/webhook/"))
        {
            return new RateLimitConfig(100, 60); // 100 requests per minute
        }
        
        // Display page - moderate limits (auto-refresh every 10s)
        if (path.Contains("/display"))
        {
            return new RateLimitConfig(600, 60); // 600 requests per minute (~10 clients refreshing)
        }
        
        // API endpoints - moderate limits
        if (path.Contains("/api/") && path.Contains("/bitcoin-rewards/"))
        {
            return new RateLimitConfig(300, 60); // 300 requests per minute
        }
        
        // Metrics endpoint - generous limits (Prometheus scraping)
        if (path.Contains("/metrics"))
        {
            return new RateLimitConfig(1000, 60); // 1000 requests per minute
        }
        
        // No rate limit for other paths
        return null;
    }
    
    private string GetClientIdentifier(HttpContext context)
    {
        // Try to get real IP from headers (behind proxy)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }
        
        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }
        
        // Fall back to connection remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
    
    private void MaybeCleanup()
    {
        if (DateTime.UtcNow - _lastCleanup < CleanupInterval)
            return;
        
        _lastCleanup = DateTime.UtcNow;
        
        // Remove entries older than 10 minutes
        var cutoff = DateTime.UtcNow.AddMinutes(-10);
        var toRemove = _rateLimits
            .Where(kv => kv.Value.WindowStart < cutoff)
            .Select(kv => kv.Key)
            .ToList();
        
        foreach (var key in toRemove)
        {
            _rateLimits.TryRemove(key, out _);
        }
        
        if (toRemove.Any())
        {
            _logger.LogDebug("Cleaned up {Count} expired rate limit entries", toRemove.Count);
        }
    }
}

/// <summary>
/// Rate limit configuration for an endpoint
/// </summary>
public class RateLimitConfig
{
    public int MaxRequests { get; }
    public int WindowSeconds { get; }
    
    public RateLimitConfig(int maxRequests, int windowSeconds)
    {
        MaxRequests = maxRequests;
        WindowSeconds = windowSeconds;
    }
}

/// <summary>
/// Rate limit entry tracking request counts in a time window
/// </summary>
public class RateLimitEntry
{
    public DateTime WindowStart { get; set; }
    public int Count { get; set; }
}
