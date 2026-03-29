using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace BTCPayServer.Plugins.BitcoinRewards.Middleware
{
    /// <summary>
    /// Middleware to generate and propagate correlation IDs across requests
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
            // Check if correlation ID already exists (from client or upstream proxy)
            var correlationId = GetOrCreateCorrelationId(context);
            
            // Store in HttpContext.Items for access by other middleware/services
            context.Items["CorrelationId"] = correlationId;
            
            // Add to response headers (helps with debugging)
            context.Response.OnStarting(() =>
            {
                if (!context.Response.Headers.ContainsKey(CORRELATION_ID_HEADER))
                {
                    context.Response.Headers.Add(CORRELATION_ID_HEADER, correlationId);
                }
                return Task.CompletedTask;
            });
            
            // Create a log scope for this request
            using (_logger.BeginScope(new { CorrelationId = correlationId }))
            {
                _logger.LogDebug("Request started: {Method} {Path} [CorrelationId: {CorrelationId}]",
                    context.Request.Method, context.Request.Path, correlationId);
                
                await _next(context);
                
                _logger.LogDebug("Request completed: {Method} {Path} {StatusCode} [CorrelationId: {CorrelationId}]",
                    context.Request.Method, context.Request.Path, context.Response.StatusCode, correlationId);
            }
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
