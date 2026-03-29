using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using BTCPayServer.Plugins.BitcoinRewards.Middleware;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Logging
{
    /// <summary>
    /// Log enricher that adds Bitcoin Rewards context to log messages
    /// </summary>
    public class BitcoinRewardsLogEnricher
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        
        public BitcoinRewardsLogEnricher(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }
        
        /// <summary>
        /// Create a structured logging scope with Bitcoin Rewards context
        /// </summary>
        public IDisposable? CreateScope(ILogger logger, string? storeId = null, string? rewardId = null)
        {
            var context = _httpContextAccessor.HttpContext;
            var correlationId = context?.GetCorrelationId();
            var clientIp = GetClientIp(context);
            var userAgent = context?.Request.Headers["User-Agent"].ToString();
            
            var enrichedContext = new
            {
                Plugin = "BitcoinRewards",
                CorrelationId = correlationId,
                StoreId = storeId,
                RewardId = rewardId,
                ClientIp = clientIp,
                UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
                Timestamp = DateTime.UtcNow
            };
            
            return logger.BeginScope(enrichedContext);
        }
        
        /// <summary>
        /// Get client IP address from HttpContext
        /// </summary>
        private static string? GetClientIp(HttpContext? context)
        {
            if (context == null)
                return null;
            
            // Check X-Forwarded-For header first
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(forwardedFor))
            {
                var ips = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (ips.Length > 0)
                    return ips[0].Trim();
            }
            
            return context.Connection.RemoteIpAddress?.ToString();
        }
    }
    
    /// <summary>
    /// Extension methods for structured logging with Bitcoin Rewards context
    /// </summary>
    public static class LoggingExtensions
    {
        /// <summary>
        /// Log with structured Bitcoin Rewards context
        /// </summary>
        public static void LogBitcoinRewards(
            this ILogger logger,
            LogLevel logLevel,
            string message,
            string? storeId = null,
            string? rewardId = null,
            string? transactionId = null,
            Exception? exception = null)
        {
            var enrichedState = new
            {
                Message = message,
                Plugin = "BitcoinRewards",
                StoreId = storeId,
                RewardId = rewardId,
                TransactionId = transactionId,
                Timestamp = DateTime.UtcNow
            };
            
            logger.Log(logLevel, exception, "{@Context}", enrichedState);
        }
        
        /// <summary>
        /// Log information with Bitcoin Rewards context
        /// </summary>
        public static void LogBitcoinRewardsInfo(
            this ILogger logger,
            string message,
            string? storeId = null,
            string? rewardId = null,
            string? transactionId = null)
        {
            logger.LogBitcoinRewards(LogLevel.Information, message, storeId, rewardId, transactionId);
        }
        
        /// <summary>
        /// Log warning with Bitcoin Rewards context
        /// </summary>
        public static void LogBitcoinRewardsWarning(
            this ILogger logger,
            string message,
            string? storeId = null,
            string? rewardId = null,
            string? transactionId = null)
        {
            logger.LogBitcoinRewards(LogLevel.Warning, message, storeId, rewardId, transactionId);
        }
        
        /// <summary>
        /// Log error with Bitcoin Rewards context
        /// </summary>
        public static void LogBitcoinRewardsError(
            this ILogger logger,
            string message,
            Exception? exception = null,
            string? storeId = null,
            string? rewardId = null,
            string? transactionId = null)
        {
            logger.LogBitcoinRewards(LogLevel.Error, message, storeId, rewardId, transactionId, exception);
        }
    }
}
