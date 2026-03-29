using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services
{
    /// <summary>
    /// Service for sending webhooks to external systems
    /// </summary>
    public class WebhookOutService
    {
        private readonly StoreRepository _storeRepository;
        private readonly HttpClient _httpClient;
        private readonly ILogger<WebhookOutService> _logger;
        private readonly RewardMetrics _metrics;
        
        public WebhookOutService(
            StoreRepository storeRepository,
            HttpClient httpClient,
            ILogger<WebhookOutService> logger,
            RewardMetrics metrics)
        {
            _storeRepository = storeRepository;
            _httpClient = httpClient;
            _logger = logger;
            _metrics = metrics;
        }
        
        /// <summary>
        /// Send webhook for reward created event
        /// </summary>
        public async Task SendRewardCreatedAsync(
            string storeId,
            BitcoinRewardRecord reward)
        {
            var eventData = new RewardCreatedEventData
            {
                RewardId = reward.Id.ToString(),
                TransactionId = reward.TransactionId,
                Platform = reward.Platform.ToString(),
                TransactionAmount = reward.TransactionAmount,
                Currency = reward.Currency,
                RewardAmountSatoshis = reward.RewardAmountSatoshis,
                ClaimLink = reward.ClaimLink ?? string.Empty,
                CreatedAt = reward.CreatedAt
            };
            
            await SendWebhookAsync(storeId, WebhookEventType.RewardCreated, eventData);
        }
        
        /// <summary>
        /// Send webhook for reward claimed event
        /// </summary>
        public async Task SendRewardClaimedAsync(
            string storeId,
            BitcoinRewardRecord reward)
        {
            var eventData = new RewardClaimedEventData
            {
                RewardId = reward.Id.ToString(),
                AmountSatoshis = reward.RewardAmountSatoshis,
                ClaimedAt = reward.ClaimedAt ?? DateTime.UtcNow,
                ClaimDurationMinutes = reward.ClaimedAt.HasValue
                    ? (reward.ClaimedAt.Value - reward.CreatedAt).TotalMinutes
                    : 0
            };
            
            await SendWebhookAsync(storeId, WebhookEventType.RewardClaimed, eventData);
        }
        
        /// <summary>
        /// Send webhook for reward expired event
        /// </summary>
        public async Task SendRewardExpiredAsync(
            string storeId,
            BitcoinRewardRecord reward)
        {
            var eventData = new RewardExpiredEventData
            {
                RewardId = reward.Id.ToString(),
                AmountSatoshis = reward.RewardAmountSatoshis,
                CreatedAt = reward.CreatedAt,
                ExpiredAt = DateTime.UtcNow
            };
            
            await SendWebhookAsync(storeId, WebhookEventType.RewardExpired, eventData);
        }
        
        /// <summary>
        /// Send webhook with automatic retry logic
        /// </summary>
        private async Task SendWebhookAsync(
            string storeId,
            WebhookEventType eventType,
            object eventData)
        {
            // Get webhook configuration
            var config = await _storeRepository.GetSettingAsync<WebhookOutConfiguration>(
                storeId,
                "BitcoinRewardsWebhookOutConfig");
            
            if (config == null || !config.Enabled)
            {
                _logger.LogDebug("Webhook out not configured or disabled for store {StoreId}", storeId);
                return;
            }
            
            if (!config.SubscribedEvents.Contains(eventType))
            {
                _logger.LogDebug("Store {StoreId} not subscribed to event {EventType}", 
                    storeId, eventType);
                return;
            }
            
            // Build payload
            var payload = new WebhookOutPayload
            {
                EventType = eventType.ToString(),
                StoreId = storeId,
                Data = eventData,
                Metadata = new()
                {
                    ["version"] = "1.5.0",
                    ["plugin"] = "BitcoinRewards"
                }
            };
            
            // Serialize
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            // Calculate signature
            var signature = CalculateSignature(json, config.Secret);
            
            // Send with retry logic
            for (int attempt = 1; attempt <= config.MaxRetries; attempt++)
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, config.Url)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    };
                    
                    request.Headers.Add("X-Bitcoin-Rewards-Signature", signature);
                    request.Headers.Add("X-Bitcoin-Rewards-Event", eventType.ToString());
                    request.Headers.Add("X-Bitcoin-Rewards-Delivery", payload.EventId);
                    
                    _httpClient.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
                    
                    var response = await _httpClient.SendAsync(request);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("Webhook delivered successfully: {EventType} to {Url} (attempt {Attempt})",
                            eventType, config.Url, attempt);
                        
                        _metrics.RecordWebhookDuration("outgoing", storeId, 0, true);
                        return;
                    }
                    
                    _logger.LogWarning("Webhook delivery failed: {EventType} to {Url} (attempt {Attempt}/{Max}), status {Status}",
                        eventType, config.Url, attempt, config.MaxRetries, response.StatusCode);
                    
                    if (attempt < config.MaxRetries)
                    {
                        // Exponential backoff: 1s, 2s, 4s
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                        await Task.Delay(delay);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Webhook delivery error: {EventType} to {Url} (attempt {Attempt}/{Max})",
                        eventType, config.Url, attempt, config.MaxRetries);
                    
                    if (attempt < config.MaxRetries)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                        await Task.Delay(delay);
                    }
                }
            }
            
            // All attempts failed
            _metrics.RecordWebhookDuration("outgoing", storeId, 0, false);
            _metrics.RecordError("webhook_out", storeId, "max_retries_exceeded");
            _logger.LogError("Webhook delivery failed after {MaxRetries} attempts: {EventType} to {Url}",
                config.MaxRetries, eventType, config.Url);
        }
        
        /// <summary>
        /// Calculate HMAC-SHA256 signature for webhook payload
        /// </summary>
        private static string CalculateSignature(string payload, string secret)
        {
            var keyBytes = Encoding.UTF8.GetBytes(secret);
            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            
            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(payloadBytes);
            return Convert.ToBase64String(hash);
        }
        
        /// <summary>
        /// Test webhook configuration
        /// </summary>
        public async Task<bool> TestWebhookAsync(string storeId, WebhookOutConfiguration config)
        {
            var testPayload = new WebhookOutPayload
            {
                EventType = "test",
                StoreId = storeId,
                Data = new { message = "Test webhook from Bitcoin Rewards plugin" }
            };
            
            var json = JsonSerializer.Serialize(testPayload);
            var signature = CalculateSignature(json, config.Secret);
            
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, config.Url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                
                request.Headers.Add("X-Bitcoin-Rewards-Signature", signature);
                request.Headers.Add("X-Bitcoin-Rewards-Event", "test");
                
                _httpClient.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Test webhook delivered successfully to {Url}", config.Url);
                    return true;
                }
                
                _logger.LogWarning("Test webhook failed: {Status}", response.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test webhook error for {Url}", config.Url);
                return false;
            }
        }
    }
}
