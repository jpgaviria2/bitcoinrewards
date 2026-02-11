#nullable enable
using System;

namespace BTCPayServer.Plugins.BitcoinRewards.ViewModels;

public class DisplayRewardsViewModel
{
    public string StoreId { get; set; } = string.Empty;
    public bool HasReward { get; set; }
    public string? LnurlQrDataUri { get; set; }
    public string? ClaimLink { get; set; }
    public long RewardAmountSatoshis { get; set; }
    public decimal RewardAmountBtc { get; set; }
    public string? OrderId { get; set; }
    public DateTime? CreatedAt { get; set; }
    public int AutoRefreshSeconds { get; set; } = 10;
    public int TimeframeMinutes { get; set; } = 60;
    public int DisplayTimeoutSeconds { get; set; } = 60;
    public int RemainingSeconds { get; set; }
    public string? PullPaymentId { get; set; }
    public string? CustomTemplate { get; set; }
    public string? LnurlString { get; set; }
    
    // Bolt Card NFC tap support
    public bool BoltCardEnabled { get; set; }
    public string? RewardId { get; set; }
}

