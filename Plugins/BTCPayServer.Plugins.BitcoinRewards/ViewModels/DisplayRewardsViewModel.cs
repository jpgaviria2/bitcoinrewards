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
    
    // Waiting screen template
    public string? WaitingTemplate { get; set; }
    
    // Branding properties
    public string? StoreName { get; set; }
    public string PrimaryColor { get; set; } = "#6B4423";
    public string SecondaryColor { get; set; } = "#CD853F";
    public string AccentColor { get; set; } = "#F5F5DC";
    public string? LogoUrl { get; set; }
    
    // Bolt Card NFC tap support
    public bool BoltCardEnabled { get; set; }
    public string? RewardId { get; set; }
}

