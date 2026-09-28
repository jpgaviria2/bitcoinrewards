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
    Square = 2,
    Btcpay = 4,
    All = Square | Btcpay
}

public class BitcoinRewardsStoreSettings
{
    public const string SettingsName = "BitcoinRewardsPluginSettings";
    
    /// <summary>
    /// Whether the plugin is enabled for this store
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// Reward percentage (0-100) for Square. Kept for backward compatibility.
    /// </summary>
    public decimal RewardPercentage { get; set; } = 0m;

    /// <summary>
    /// Reward percentage (0-100) for Square.
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
    /// Platforms enabled (Square and/or BTCPay)
    /// </summary>
    public PlatformFlags EnabledPlatforms { get; set; } = PlatformFlags.None;
    
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
    /// Optional minimum reward floor in sats. Useful for direct Lightning payouts where very small rewards can be rejected by BTCPay or the recipient wallet.
    /// </summary>
    public long? MinimumRewardSatoshis { get; set; }
    
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

    // ── Rewards Part B (iOS customer profile integration) ──

    /// <summary>
    /// Enables the additive customer-profile/order-association API. This is deliberately
    /// independent from direct payout and defaults off for safe rollout.
    /// </summary>
    public bool CustomerProfileAssociationEnabled { get; set; } = false;

    /// <summary>Base URL of the optional customer profile service.</summary>
    public string CustomerProfileApiBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Service credential used only for server-to-server profile resolution. It is never
    /// returned by an API or written to logs.
    /// </summary>
    public string? CustomerProfileApiToken { get; set; }

    /// <summary>
    /// Future rollout control for direct LNURL-pay delivery. The Part B foundation does not
    /// activate direct payouts; operators must leave this false until the payout saga ships.
    /// </summary>
    public bool DirectLightningPayoutEnabled { get; set; } = false;

    /// <summary>
    /// Keep the existing pull-payment reward path authoritative while Part B is being proven.
    /// </summary>
    public bool LegacyPullPaymentFallbackEnabled { get; set; } = true;

    /// <summary>
    /// Enables the customer-facing wallet QR check-in page. A scanned Lightning address is
    /// held briefly and consumed by the next Square completed payment for this store.
    /// </summary>
    public bool CustomerLightningCheckInEnabled { get; set; } = true;

    /// <summary>
    /// How long a scanned customer Lightning address can wait for the next Square order.
    /// </summary>
    public int CustomerLightningCheckInTtlMinutes { get; set; } = 5;

    /// <summary>
    /// Comma-separated Lightning address domains allowed for wallet QR check-in.
    /// </summary>
    public string CustomerLightningCheckInAllowedDomains { get; set; } = string.Empty;
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
    
    /// <summary>
    /// Custom HTML template for the waiting/idle screen when no rewards are pending (optional)
    /// </summary>
    public string? WaitingTemplateOverride { get; set; }
    
    // ── Branding Settings ──
    
    /// <summary>
    /// Primary brand color (hex code, e.g., "#6B4423")
    /// </summary>
    public string PrimaryColor { get; set; } = "#6B4423";
    
    /// <summary>
    /// Secondary brand color (hex code, e.g., "#CD853F")
    /// </summary>
    public string SecondaryColor { get; set; } = "#CD853F";
    
    /// <summary>
    /// Accent color (hex code, e.g., "#F5F5DC")
    /// </summary>
    public string AccentColor { get; set; } = "#F5F5DC";
    
    /// <summary>
    /// Logo URL for branding (optional)
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Arms a one-time 10 sat admin test. The next scanned Lightning address receives the test reward automatically.
    /// </summary>
    public bool OneTimeScannedAddressTestEnabled { get; set; } = false;

    /// <summary>
    /// Reward size used by admin-only live tests.
    /// </summary>
    public long TestRewardSatoshis { get; set; } = 10;

    // ── Dual Balance Settings ──

    /// <summary>Default auto-convert setting for new wallets.</summary>
    public bool DefaultAutoConvertToCad { get; set; } = true;

    /// <summary>CAD spending enabled (allows POS debit of CAD balance).</summary>
    public bool CadSpendingEnabled { get; set; } = false;

    /// <summary>Allow customers to swap between CAD and sats.</summary>
    public bool SwapEnabled { get; set; } = true;
}

public class SquareApiCredentials
{
    public string? ApplicationId { get; set; }
    public string? AccessToken { get; set; }
    public string? LocationId { get; set; }
    public string? Environment { get; set; }
    public string? WebhookSignatureKey { get; set; }
}

public class SmsProviderConfig
{
    public string? Provider { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? FromNumber { get; set; }
}
