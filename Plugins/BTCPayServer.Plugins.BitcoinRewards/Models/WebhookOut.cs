using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.BitcoinRewards.Models
{
    /// <summary>
    /// Outgoing webhook configuration
    /// </summary>
    public class WebhookOutConfiguration
    {
        public string Url { get; set; } = string.Empty;
        public string Secret { get; set; } = string.Empty; // For HMAC signature
        public bool Enabled { get; set; } = true;
        public List<WebhookEventType> SubscribedEvents { get; set; } = new();
        public int TimeoutSeconds { get; set; } = 30;
        public int MaxRetries { get; set; } = 3;
    }
    
    /// <summary>
    /// Types of events that can trigger webhooks
    /// </summary>
    public enum WebhookEventType
    {
        RewardCreated,
        RewardClaimed,
        RewardExpired,
        RewardFailed,
        ErrorOccurred
    }
    
    /// <summary>
    /// Outgoing webhook payload
    /// </summary>
    public class WebhookOutPayload
    {
        public string EventId { get; set; } = Guid.NewGuid().ToString();
        public string EventType { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string StoreId { get; set; } = string.Empty;
        public object Data { get; set; } = new();
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
    
    /// <summary>
    /// Reward created event data
    /// </summary>
    public class RewardCreatedEventData
    {
        public string RewardId { get; set; } = string.Empty;
        public string TransactionId { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public decimal TransactionAmount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public long RewardAmountSatoshis { get; set; }
        public string ClaimLink { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
    
    /// <summary>
    /// Reward claimed event data
    /// </summary>
    public class RewardClaimedEventData
    {
        public string RewardId { get; set; } = string.Empty;
        public long AmountSatoshis { get; set; }
        public DateTime ClaimedAt { get; set; }
        public double ClaimDurationMinutes { get; set; }
    }
    
    /// <summary>
    /// Reward expired event data
    /// </summary>
    public class RewardExpiredEventData
    {
        public string RewardId { get; set; } = string.Empty;
        public long AmountSatoshis { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiredAt { get; set; }
        public string Reason { get; set; } = "Unclaimed within expiration period";
    }
    
    /// <summary>
    /// Webhook delivery attempt record
    /// </summary>
    public class WebhookOutDelivery
    {
        public int Id { get; set; }
        public string StoreId { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public int Attempt { get; set; }
        public int ResponseStatusCode { get; set; }
        public string ResponseBody { get; set; } = string.Empty;
        public bool Success { get; set; }
        public DateTime AttemptedAt { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
