#nullable enable
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Plugins.BitcoinRewards;

namespace BTCPayServer.Plugins.BitcoinRewards.ViewModels;

public class BitcoinRewardsSettingsViewModel
{
    public string StoreId { get; set; } = string.Empty;
    
    [Display(Name = "Enable Bitcoin Rewards")]
    public bool Enabled { get; set; }
    
    [Display(Name = "Shopify / Square Reward Percentage")]
    [Range(0, 100, ErrorMessage = "Reward percentage must be between 0 and 100")]
    [Required(ErrorMessage = "Reward percentage is required")]
    public decimal ExternalRewardPercentage { get; set; }

    [Display(Name = "BTCPay Reward Percentage")]
    [Range(0, 100, ErrorMessage = "Reward percentage must be between 0 and 100")]
    [Required(ErrorMessage = "BTCPay reward percentage is required")]
    public decimal BtcpayRewardPercentage { get; set; }
    
    [Display(Name = "Delivery Method")]
    public DeliveryMethod DeliveryMethod { get; set; } = DeliveryMethod.Email;
    
    [Display(Name = "Enable Shopify")]
    public bool EnableShopify { get; set; }
    
    [Display(Name = "Enable Square")]
    public bool EnableSquare { get; set; }

    [Display(Name = "Enable BTCPay payments")]
    public bool EnableBtcpay { get; set; }
    
    // Shopify Settings
    [Display(Name = "Shopify Shop URL")]
    public string? ShopifyShopUrl { get; set; }
    
    [Display(Name = "Shopify Access Token")]
    public string? ShopifyAccessToken { get; set; }
    
    // Square Settings
    [Display(Name = "Square Application ID")]
    public string? SquareApplicationId { get; set; }
    
    [Display(Name = "Square Access Token")]
    public string? SquareAccessToken { get; set; }
    
    [Display(Name = "Square Location ID")]
    public string? SquareLocationId { get; set; }
    
    [Display(Name = "Square Environment")]
    public string? SquareEnvironment { get; set; }

    [Display(Name = "Square Webhook Signature Key")]
    public string? SquareWebhookSignatureKey { get; set; }

    public bool HasSquareAccessToken { get; set; }
    public bool HasSquareWebhookSignatureKey { get; set; }
    
    // Email Settings
    [Display(Name = "Email Subject Override (Optional)")]
    public string? EmailSubjectOverride { get; set; }

    [Display(Name = "Email Template Override (Optional)")]
    public string? EmailTemplateOverride { get; set; }
    
    // SMS Settings (Future)
    [Display(Name = "SMS Provider")]
    public string? SmsProvider { get; set; }
    
    [Display(Name = "SMS API Key")]
    public string? SmsApiKey { get; set; }
    
    [Display(Name = "SMS API Secret")]
    public string? SmsApiSecret { get; set; }
    
    [Display(Name = "SMS From Number")]
    public string? SmsFromNumber { get; set; }
    
    // Advanced Settings
    [Display(Name = "Minimum Transaction Amount")]
    [Range(0, double.MaxValue, ErrorMessage = "Minimum transaction amount must be positive")]
    public decimal? MinimumTransactionAmount { get; set; }
    
    [Display(Name = "Maximum Reward (Satoshis)")]
    [Range(0, long.MaxValue, ErrorMessage = "Maximum reward must be positive")]
    public long? MaximumRewardSatoshis { get; set; }
    
    // Payout Processor Settings
    [Display(Name = "Selected Payout Processor")]
    public string? SelectedPayoutProcessorId { get; set; }
    
    [Display(Name = "Available Payout Processors")]
    public List<PayoutProcessorOption> AvailablePayoutProcessors { get; set; } = new();

    // Fallback base URL for claim links
    [Display(Name = "Server Base URL (fallback)")]
    [Url(ErrorMessage = "Enter a valid absolute URL (e.g., https://yourdomain.com/)")]
    public string? ServerBaseUrl { get; set; }
    
    // Physical Store Display Settings
    [Display(Name = "Enable Display Mode")]
    public bool EnableDisplayMode { get; set; }
    
    [Display(Name = "Fallback to Display When No Email/Phone")]
    public bool FallbackToDisplayWhenNoEmail { get; set; } = true;
    
    [Display(Name = "Display Duration (seconds)")]
    [Range(10, 300, ErrorMessage = "Display duration must be between 10 and 300 seconds")]
    public int DisplayDurationSeconds { get; set; } = 60;
    
    public PlatformFlags GetEnabledPlatforms()
    {
        PlatformFlags flags = PlatformFlags.None;
        if (EnableShopify) flags |= PlatformFlags.Shopify;
        if (EnableSquare) flags |= PlatformFlags.Square;
        if (EnableBtcpay) flags |= PlatformFlags.Btcpay;
        return flags;
    }
    
    public void SetFromSettings(BitcoinRewardsStoreSettings settings)
    {
        if (settings == null)
        {
            // Use defaults if settings are null
            Enabled = false;
            ExternalRewardPercentage = 0m;
            BtcpayRewardPercentage = 0m;
            DeliveryMethod = DeliveryMethod.Email;
            EnableShopify = false;
            EnableSquare = false;
            EnableBtcpay = false;
            return;
        }
        
        Enabled = settings.Enabled;
        ExternalRewardPercentage = settings.ExternalRewardPercentage > 0 ? settings.ExternalRewardPercentage : settings.RewardPercentage;
        BtcpayRewardPercentage = settings.BtcpayRewardPercentage > 0 ? settings.BtcpayRewardPercentage : settings.RewardPercentage;
        DeliveryMethod = settings.DeliveryMethod;
        
        // Shopify temporarily disabled
        EnableShopify = false;
        EnableSquare = (settings.EnabledPlatforms & PlatformFlags.Square) == PlatformFlags.Square;
        EnableBtcpay = (settings.EnabledPlatforms & PlatformFlags.Btcpay) == PlatformFlags.Btcpay;
        
        ShopifyShopUrl = settings.Shopify?.ShopUrl;
        ShopifyAccessToken = settings.Shopify?.AccessToken;
        
        SquareApplicationId = settings.Square?.ApplicationId;
        SquareAccessToken = settings.Square?.AccessToken;
        SquareLocationId = settings.Square?.LocationId;
        SquareEnvironment = settings.Square?.Environment;
        SquareWebhookSignatureKey = settings.Square?.WebhookSignatureKey;

        HasSquareAccessToken = !string.IsNullOrWhiteSpace(settings.Square?.AccessToken);
        HasSquareWebhookSignatureKey = !string.IsNullOrWhiteSpace(settings.Square?.WebhookSignatureKey);

        // Never echo secrets back into the form fields
        SquareAccessToken = null;
        SquareWebhookSignatureKey = null;
        
        EmailSubjectOverride = settings.EmailSubject;
        EmailTemplateOverride = settings.EmailTemplate;
        ServerBaseUrl = settings.ServerBaseUrl;
        
        SmsProvider = settings.SmsProvider?.Provider;
        SmsApiKey = settings.SmsProvider?.ApiKey;
        SmsApiSecret = settings.SmsProvider?.ApiSecret;
        SmsFromNumber = settings.SmsProvider?.FromNumber;
        
        MinimumTransactionAmount = settings.MinimumTransactionAmount;
        MaximumRewardSatoshis = settings.MaximumRewardSatoshis;
        SelectedPayoutProcessorId = settings.SelectedPayoutProcessorId;
        
        EnableDisplayMode = settings.EnableDisplayMode;
        FallbackToDisplayWhenNoEmail = settings.FallbackToDisplayWhenNoEmail;
        DisplayDurationSeconds = settings.DisplayDurationSeconds;
    }
    
    public BitcoinRewardsStoreSettings ToSettings(BitcoinRewardsStoreSettings? existing = null)
    {
        var settings = existing ?? new BitcoinRewardsStoreSettings();

        settings.Enabled = Enabled;
        settings.RewardPercentage = ExternalRewardPercentage;
        settings.ExternalRewardPercentage = ExternalRewardPercentage;
        settings.BtcpayRewardPercentage = BtcpayRewardPercentage;
        settings.DeliveryMethod = DeliveryMethod;
        settings.EnabledPlatforms = GetEnabledPlatforms();
        settings.EmailSubject = EmailSubjectOverride;
        settings.EmailTemplate = EmailTemplateOverride;
        settings.MinimumTransactionAmount = MinimumTransactionAmount;
        settings.MaximumRewardSatoshis = MaximumRewardSatoshis;
        settings.SelectedPayoutProcessorId = SelectedPayoutProcessorId;
        settings.ServerBaseUrl = string.IsNullOrWhiteSpace(ServerBaseUrl) ? null : ServerBaseUrl!.Trim();
        
        settings.EnableDisplayMode = EnableDisplayMode;
        settings.FallbackToDisplayWhenNoEmail = FallbackToDisplayWhenNoEmail;
        settings.DisplayDurationSeconds = DisplayDurationSeconds;
        
        if (EnableShopify)
        {
            settings.Shopify = new ShopifyApiCredentials
            {
                ShopUrl = ShopifyShopUrl,
                AccessToken = ShopifyAccessToken
            };
        }
        
        if (EnableSquare)
        {
            settings.Square ??= new SquareApiCredentials();

            if (!string.IsNullOrWhiteSpace(SquareApplicationId))
                settings.Square.ApplicationId = SquareApplicationId;
            if (!string.IsNullOrWhiteSpace(SquareAccessToken))
                settings.Square.AccessToken = SquareAccessToken;
            if (!string.IsNullOrWhiteSpace(SquareLocationId))
                settings.Square.LocationId = SquareLocationId;

            settings.Square.Environment = string.IsNullOrWhiteSpace(SquareEnvironment)
                ? settings.Square.Environment ?? "production"
                : SquareEnvironment;

            if (!string.IsNullOrWhiteSpace(SquareWebhookSignatureKey))
                settings.Square.WebhookSignatureKey = SquareWebhookSignatureKey;
        }
        
        if (DeliveryMethod == DeliveryMethod.Sms && !string.IsNullOrWhiteSpace(SmsProvider))
        {
            settings.SmsProvider = new SmsProviderConfig
            {
                Provider = SmsProvider,
                ApiKey = SmsApiKey,
                ApiSecret = SmsApiSecret,
                FromNumber = SmsFromNumber
            };
        }
        
        return settings;
    }
}

