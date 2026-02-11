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
    /// Email subject template for reward notifications (optional)
    /// </summary>
    public string? EmailSubject { get; set; }
    
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
    /// Maximum single reward transaction cap (in sats) - security limit to prevent large fraudulent rewards
    /// Default: 1,000,000 sats (0.01 BTC)
    /// </summary>
    public long MaximumSingleRewardSatoshis { get; set; } = 1_000_000;
    
    /// <summary>
    /// Selected payout processor ID for rewards (format: "{Processor}:{PayoutMethodId}")
    /// </summary>
    public string? SelectedPayoutProcessorId { get; set; }

    /// <summary>
    /// Optional fallback base URL (https://...) used to build absolute claim links when HttpContext and StoreWebsite are unavailable.
    /// </summary>
    public string? ServerBaseUrl { get; set; }
    
    // ── Bolt Card Settings ──

    /// <summary>
    /// Whether Bolt Card NFC tap-to-collect is enabled on the rewards display page.
    /// </summary>
    public bool BoltCardEnabled { get; set; } = false;

    /// <summary>
    /// Optional reference to a BoltcardFactory app used for mass card issuance.
    /// </summary>
    public string? BoltcardFactoryAppId { get; set; }

    /// <summary>
    /// Default initial balance (in sats) loaded onto each new card's pull payment.
    /// </summary>
    public long DefaultCardBalanceSats { get; set; } = 100;

    /// <summary>
    /// How long the QR code should be displayed before automatically hiding (in seconds)
    /// </summary>
    public int DisplayTimeoutSeconds { get; set; } = 60;
    
    /// <summary>
    /// How often the display page auto-refreshes (in seconds)
    /// </summary>
    public int DisplayAutoRefreshSeconds { get; set; } = 10;
    
    /// <summary>
    /// How far back to look for unclaimed rewards on the display page (in minutes)
    /// </summary>
    public int DisplayTimeframeMinutes { get; set; } = 60;
    
    /// <summary>
    /// Custom HTML template for the rewards display page (optional)
    /// </summary>
    public string? DisplayTemplateOverride { get; set; }
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
    public string? WebhookSignatureKey { get; set; }
}

public class SmsProviderConfig
{
    public string? Provider { get; set; } // "twilio", "aws-sns", etc.
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? FromNumber { get; set; }
}

