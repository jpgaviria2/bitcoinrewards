using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Middleware
{
    /// <summary>
    /// Middleware for rate limiting Bitcoin Rewards plugin endpoints
    /// </summary>
    public class RateLimitingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RateLimitingMiddleware> _logger;
        
        public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }
        
        public async Task InvokeAsync(
            HttpContext context,
            RateLimitService rateLimitService,
            StoreRepository storeRepository,
            RewardMetrics metrics)
        {
            // Only apply to Bitcoin Rewards endpoints
            if (!context.Request.Path.StartsWithSegments("/plugins/bitcoin-rewards") &&
                !context.Request.Path.StartsWithSegments("/api/v1/bitcoin-rewards"))
            {
                await _next(context);
                return;
            }
            
            // Get rate limit configuration for the store
            var storeId = ExtractStoreId(context.Request.Path);
            RateLimitConfiguration? config = null;
            
            if (!string.IsNullOrEmpty(storeId))
            {
                config = await storeRepository.GetSettingAsync<RateLimitConfiguration>(
                    storeId,
                    "BitcoinRewardsRateLimitConfig");
            }
            
            // Use default config if not found or rate limiting disabled
            config ??= new RateLimitConfiguration();
            
            if (!config.Enabled)
            {
                await _next(context);
                return;
            }
            
            // Get client IP
            var clientIp = GetClientIp(context);
            
            // Check blacklist
            if (config.BlacklistedIps.Contains(clientIp))
            {
                _logger.LogWarning("Blocked request from blacklisted IP {IP}", clientIp);
                metrics.RecordError("rate_limit", storeId ?? "unknown", "blacklisted_ip");
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                await context.Response.WriteAsync("Access denied");
                return;
            }
            
            // Check whitelist (skip rate limiting)
            if (config.WhitelistedIps.Contains(clientIp))
            {
                await _next(context);
                return;
            }
            
            // Determine which policy to apply
            var policy = DeterminePolicy(context.Request.Path, config);
            
            // Check per-IP rate limit
            var ipState = await rateLimitService.CheckRateLimitAsync(
                $"ip:{clientIp}",
                policy);
            
            // Check per-store rate limit if applicable
            RateLimitState? storeState = null;
            if (!string.IsNullOrEmpty(storeId))
            {
                storeState = await rateLimitService.CheckRateLimitAsync(
                    $"store:{storeId}",
                    config.StorePolicy);
            }
            
            // If either limit is exceeded, return 429
            var isLimited = ipState.IsLimited || (storeState?.IsLimited ?? false);
            var limitingFactor = ipState.IsLimited ? "IP" : "Store";
            var state = ipState.IsLimited ? ipState : storeState ?? ipState;
            
            // Add rate limit headers
            if (policy.IncludeHeaders)
            {
                context.Response.Headers["X-RateLimit-Limit"] = policy.RequestsPerWindow.ToString();
                context.Response.Headers["X-RateLimit-Remaining"] = state.TokensRemaining.ToString();
                context.Response.Headers["X-RateLimit-Reset"] = state.ResetAt.ToString();
                
                if (isLimited)
                {
                    var retryAfter = (DateTimeOffset.FromUnixTimeSeconds(state.ResetAt) - DateTimeOffset.UtcNow).TotalSeconds;
                    context.Response.Headers["Retry-After"] = ((int)Math.Ceiling(retryAfter)).ToString();
                }
            }
            
            if (isLimited)
            {
                _logger.LogWarning("Rate limit exceeded for {Factor} {Key} on path {Path}", 
                    limitingFactor, state.Key, context.Request.Path);
                
                metrics.RecordError("rate_limit", storeId ?? "unknown", $"{limitingFactor.ToLower()}_exceeded");
                
                context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Rate limit exceeded",
                    limitedBy = limitingFactor,
                    retryAfter = state.ResetAt,
                    message = $"Too many requests. Please try again after {DateTimeOffset.FromUnixTimeSeconds(state.ResetAt):u}"
                });
                return;
            }
            
            // Request allowed, continue pipeline
            await _next(context);
        }
        
        /// <summary>
        /// Extract store ID from request path
        /// </summary>
        private static string? ExtractStoreId(PathString path)
        {
            var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments == null || segments.Length < 3)
                return null;
            
            // Pattern: /plugins/bitcoin-rewards/{storeId}/...
            if (segments[0] == "plugins" && segments[1] == "bitcoin-rewards")
                return segments.Length > 2 ? segments[2] : null;
            
            // Pattern: /api/v1/bitcoin-rewards (no store ID in path, may be in query)
            return null;
        }
        
        /// <summary>
        /// Get client IP address, respecting X-Forwarded-For headers
        /// </summary>
        private static string GetClientIp(HttpContext context)
        {
            // Check X-Forwarded-For header (for reverse proxy scenarios)
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                // Take the first IP (original client)
                var ips = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (ips.Length > 0)
                    return ips[0].Trim();
            }
            
            // Fall back to direct connection IP
            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
        
        /// <summary>
        /// Determine which rate limit policy to apply based on endpoint
        /// </summary>
        private static RateLimitPolicy DeterminePolicy(PathString path, RateLimitConfiguration config)
        {
            var pathValue = path.Value ?? string.Empty;
            
            // Webhook endpoints (most restrictive)
            if (pathValue.Contains("/webhooks/", StringComparison.OrdinalIgnoreCase))
                return config.WebhookPolicy;
            
            // Admin/UI endpoints
            if (pathValue.Contains("/settings", StringComparison.OrdinalIgnoreCase) ||
                pathValue.Contains("/errors", StringComparison.OrdinalIgnoreCase) ||
                pathValue.Contains("/display", StringComparison.OrdinalIgnoreCase))
                return config.AdminPolicy;
            
            // API endpoints
            if (pathValue.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                return config.ApiPolicy;
            
            // Default to webhook policy (most restrictive)
            return config.WebhookPolicy;
        }
    }
}
