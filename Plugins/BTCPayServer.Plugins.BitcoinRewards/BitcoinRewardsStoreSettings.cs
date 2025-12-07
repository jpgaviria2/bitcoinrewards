#nullable enable

namespace BTCPayServer.Plugins.BitcoinRewards;

public enum DeliveryMethod
{
    Email = 0,
    Sms = 1
}

public enum PlatformFlags
{
    None = 0,
    Shopify = 1,
    Square = 2,
    Btcpay = 4,
    All = Shopify | Square | Btcpay
}

public class BitcoinRewardsStoreSettings
{
    public const string SettingsName = "BitcoinRewardsPluginSettings";
    
    /// <summary>
    /// Whether the plugin is enabled for this store
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// Reward percentage (0-100) for external platforms (Shopify/Square). Kept for backward compatibility.
    /// </summary>
    public decimal RewardPercentage { get; set; } = 0m;

    /// <summary>
    /// Reward percentage (0-100) for Shopify/Square.
    /// </summary>
    public decimal ExternalRewardPercentage { get; set; } = 0m;

    /// <summary>
    /// Reward percentage (0-100) for BTCPay-origin payments.
    /// </summary>
    public decimal BtcpayRewardPercentage { get; set; } = 0m;
    
    /// <summary>
    /// Delivery method for rewards (Email or SMS)
    /// </summary>
    public DeliveryMethod DeliveryMethod { get; set; } = DeliveryMethod.Email;
    
    /// <summary>
    /// Platforms enabled (Shopify, Square, or Both)
    /// </summary>
    public PlatformFlags EnabledPlatforms { get; set; } = PlatformFlags.None;
    
    /// <summary>
    /// Shopify API credentials (reuses existing Shopify plugin settings if available)
    /// </summary>
    public ShopifyApiCredentials? Shopify { get; set; }
    
    /// <summary>
    /// Square API credentials
    /// </summary>
    public SquareApiCredentials? Square { get; set; }
    
    /// <summary>
    /// Email template for reward notifications (optional)
    /// </summary>
    public string? EmailTemplate { get; set; }
    
    /// <summary>
    /// SMS provider configuration (for future SMS integration)
    /// </summary>
    public SmsProviderConfig? SmsProvider { get; set; }
    
    /// <summary>
    /// Minimum transaction amount to trigger reward (in store currency)
    /// </summary>
    public decimal? MinimumTransactionAmount { get; set; }
    
    /// <summary>
    /// Maximum reward cap (in BTC/sats, optional)
    /// </summary>
    public long? MaximumRewardSatoshis { get; set; }
    
    /// <summary>
    /// Selected payout processor ID for rewards (format: "{Processor}:{PayoutMethodId}")
    /// </summary>
    public string? SelectedPayoutProcessorId { get; set; }
}

public class ShopifyApiCredentials
{
    public string? ShopUrl { get; set; }
    public string? AccessToken { get; set; }
}

public class SquareApiCredentials
{
    public string? ApplicationId { get; set; }
    public string? AccessToken { get; set; }
    public string? LocationId { get; set; }
    public string? Environment { get; set; } // "sandbox" or "production"
}

public class SmsProviderConfig
{
    public string? Provider { get; set; } // "twilio", "aws-sns", etc.
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? FromNumber { get; set; }
}

