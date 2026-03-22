using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Middleware;

/// <summary>
/// Rate limiting middleware to prevent spam and DOS attacks on wallet API endpoints.
/// Uses sliding window algorithm with in-memory storage.
/// </summary>
public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitMiddleware> _logger;
    
    // In-memory rate limit tracking: key = client identifier, value = request timestamps
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _requestHistory = new();
    
    // Rate limit rules per endpoint pattern
    private static readonly (string Pattern, int MaxRequests, TimeSpan Window)[] _rateLimits = new[]
    {
        // Wallet creation: 5 per hour per IP (prevent wallet spam)
        ("/plugins/bitcoin-rewards/wallet/create", 5, TimeSpan.FromHours(1)),
        
        // Payments: 20 per minute per wallet (prevent payment spam)
        ("/plugins/bitcoin-rewards/wallet/{id}/pay-invoice", 20, TimeSpan.FromMinutes(1)),
        
        // Claims: 30 per minute per wallet (prevent claim spam)
        ("/plugins/bitcoin-rewards/wallet/{id}/claim-lnurl", 30, TimeSpan.FromMinutes(1)),
        
        // Swaps: 10 per minute per wallet (prevent swap spam)
        ("/plugins/bitcoin-rewards/wallet/{id}/swap", 10, TimeSpan.FromMinutes(1)),
        
        // Balance queries: 60 per minute per wallet (allow frequent polling)
        ("/plugins/bitcoin-rewards/wallet/{id}/balance", 60, TimeSpan.FromMinutes(1)),
        
        // Settings: 10 per hour per wallet (prevent toggle spam)
        ("/plugins/bitcoin-rewards/wallet/{id}/settings", 10, TimeSpan.FromHours(1)),
        
        // NIP-05: check username availability
        ("/plugins/bitcoin-rewards/nip05/check", 20, TimeSpan.FromMinutes(1)),
        
        // NIP-05: update username (per IP since wallet auth is separate)
        ("/plugins/bitcoin-rewards/nip05/update", 3, TimeSpan.FromDays(1)),
    };

    public RateLimitMiddleware(RequestDelegate next, ILogger<RateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant();
        if (string.IsNullOrEmpty(path))
        {
            await _next(context);
            return;
        }

        // Find matching rate limit rule
        var rule = FindMatchingRule(path);
        if (rule == null)
        {
            // No rate limit for this endpoint
            await _next(context);
            return;
        }

        // Determine client identifier (wallet ID for wallet operations, IP for creation)
        var clientId = GetClientIdentifier(context, rule.Value.Pattern);
        
        // Check rate limit
        if (!IsAllowed(clientId, rule.Value.MaxRequests, rule.Value.Window))
        {
            _logger.LogWarning("Rate limit exceeded for client {ClientId} on {Path}", clientId, path);
            
            context.Response.StatusCode = 429; // Too Many Requests
            context.Response.ContentType = "application/json";
            context.Response.Headers.Append("Retry-After", ((int)rule.Value.Window.TotalSeconds).ToString());
            await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
            {
                error = "Rate limit exceeded",
                message = $"Too many requests. Limit: {rule.Value.MaxRequests} per {rule.Value.Window.TotalMinutes} minute(s)",
                retryAfter = rule.Value.Window.TotalSeconds
            }));
            return;
        }

        // Track this request
        TrackRequest(clientId);
        
        await _next(context);
    }

    private (string Pattern, int MaxRequests, TimeSpan Window)? FindMatchingRule(string path)
    {
        foreach (var rule in _rateLimits)
        {
            // Simple pattern matching: replace {id} with wildcard
            var pattern = rule.Pattern.Replace("{id}", "*");
            if (PathMatches(path, pattern))
            {
                return rule;
            }
        }
        return null;
    }

    private bool PathMatches(string path, string pattern)
    {
        if (pattern.Contains("*"))
        {
            var parts = pattern.Split('*');
            return path.Contains(parts[0]) && (parts.Length == 1 || path.Contains(parts[1]));
        }
        return path == pattern;
    }

    private string GetClientIdentifier(HttpContext context, string pattern)
    {
        // For wallet operations, use wallet ID from path
        if (pattern.Contains("{id}"))
        {
            var segments = context.Request.Path.Value?.Split('/') ?? Array.Empty<string>();
            var walletIdIndex = Array.IndexOf(segments, "wallet") + 1;
            if (walletIdIndex > 0 && walletIdIndex < segments.Length)
            {
                return $"wallet:{segments[walletIdIndex]}";
            }
        }
        
        // For wallet creation, use IP address
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{ipAddress}";
    }

    private bool IsAllowed(string clientId, int maxRequests, TimeSpan window)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - window;
        
        var history = _requestHistory.GetOrAdd(clientId, _ => new ConcurrentQueue<DateTime>());
        
        // Remove old requests outside the window
        while (history.TryPeek(out var oldest) && oldest < cutoff)
        {
            history.TryDequeue(out _);
        }
        
        // Check if limit exceeded
        return history.Count < maxRequests;
    }

    private void TrackRequest(string clientId)
    {
        var history = _requestHistory.GetOrAdd(clientId, _ => new ConcurrentQueue<DateTime>());
        history.Enqueue(DateTime.UtcNow);
        
        // Cleanup: if queue gets too large, trim old entries
        if (history.Count > 1000)
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromHours(2);
            while (history.TryPeek(out var oldest) && oldest < cutoff)
            {
                history.TryDequeue(out _);
            }
        }
    }

    /// <summary>
    /// Background cleanup task to remove expired client histories (call from hosted service)
    /// </summary>
    public static void CleanupExpiredHistories()
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromHours(24);
        var keysToRemove = _requestHistory
            .Where(kvp => kvp.Value.All(ts => ts < cutoff))
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var key in keysToRemove)
        {
            _requestHistory.TryRemove(key, out _);
        }
    }
}
