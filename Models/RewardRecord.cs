using System;

namespace BTCPayServer.Plugins.BitcoinRewards.Models
{
    public class RewardRecord
    {
        public string Id { get; set; }
        public string OrderId { get; set; }
        public string StoreId { get; set; }
        public string CustomerEmail { get; set; }
        public string CustomerPhone { get; set; }
        public decimal RewardAmount { get; set; }
        public string BitcoinAddress { get; set; }
        public string TransactionId { get; set; }
        public RewardStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
        public string Source { get; set; } // "shopify" or "square"
    }

    public enum RewardStatus
    {
        Pending,
        Processing,
        Sent,
        Failed
    }
}

