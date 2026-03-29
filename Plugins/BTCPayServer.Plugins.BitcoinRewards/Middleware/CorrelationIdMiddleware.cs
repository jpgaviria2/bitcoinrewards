using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace BTCPayServer.Plugins.BitcoinRewards.Middleware
{
    /// <summary>
    /// Middleware to generate and propagate correlation IDs across requests.
    /// Wrapped in try/catch to never crash the request pipeline.
    /// </summary>
    public class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<CorrelationIdMiddleware> _logger;
        private const string CORRELATION_ID_HEADER = "X-Correlation-Id";
        
        public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }
        
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                // Check if correlation ID already exists (from client or upstream proxy)
                var correlationId = GetOrCreateCorrelationId(context);
                
                // Store in HttpContext.Items for access by other middleware/services
                context.Items["CorrelationId"] = correlationId;
                
                // Add to response headers (helps with debugging)
                context.Response.OnStarting(() =>
                {
                    try
                    {
                        context.Response.Headers[CORRELATION_ID_HEADER] = correlationId;
                    }
                    catch
                    {
                        // Never fail on header write
                    }
                    return Task.CompletedTask;
                });
            }
            catch (Exception ex)
            {
                // Log but never crash - correlation IDs are optional enhancement
                _logger.LogDebug(ex, "Failed to set correlation ID");
            }
            
            // Always call next, even if correlation ID setup failed
            await _next(context);
        }
        
        /// <summary>
        /// Get correlation ID from request headers or generate a new one
        /// </summary>
        private static string GetOrCreateCorrelationId(HttpContext context)
        {
            // Check if client provided correlation ID
            if (context.Request.Headers.TryGetValue(CORRELATION_ID_HEADER, out StringValues correlationId) &&
                !string.IsNullOrWhiteSpace(correlationId))
            {
                return correlationId.ToString();
            }
            
            // Generate new correlation ID (short format for readability in logs)
            return GenerateShortGuid();
        }
        
        /// <summary>
        /// Generate a short, URL-safe GUID (13 characters)
        /// </summary>
        private static string GenerateShortGuid()
        {
            var bytes = Guid.NewGuid().ToByteArray();
            var base64 = Convert.ToBase64String(bytes)
                .Replace("/", "_")
                .Replace("+", "-")
                .Substring(0, 13);
            return base64;
        }
    }
    
    /// <summary>
    /// Extension methods for accessing correlation ID
    /// </summary>
    public static class CorrelationIdExtensions
    {
        /// <summary>
        /// Get correlation ID from HttpContext
        /// </summary>
        public static string? GetCorrelationId(this HttpContext context)
        {
            return context.Items.TryGetValue("CorrelationId", out var correlationId)
                ? correlationId?.ToString()
                : null;
        }
    }
}
