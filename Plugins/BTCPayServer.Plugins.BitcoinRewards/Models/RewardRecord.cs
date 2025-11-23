using System;

namespace BTCPayServer.Plugins.BitcoinRewards.Models
{
    public class RewardRecord
    {
        public string Id { get; set; } = null!;
        public string OrderId { get; set; } = null!;
        public string StoreId { get; set; } = null!;
        public string CustomerEmail { get; set; } = null!;
        public string CustomerPhone { get; set; } = null!;
        public decimal RewardAmount { get; set; }
        public string BitcoinAddress { get; set; } = null!;
        public string TransactionId { get; set; } = null!;
        public RewardStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
        public string Source { get; set; } = null!; // "shopify"
    }

    public enum RewardStatus
    {
        Pending,
        Processing,
        Sent,
        Failed
    }
}

