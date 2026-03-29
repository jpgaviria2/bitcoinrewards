#nullable enable

using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.BitcoinRewards.Exceptions;

/// <summary>
/// Categorized error types for Bitcoin Rewards operations
/// </summary>
public enum RewardErrorType
{
    // Webhook validation errors
    SquareWebhookValidationFailed,
    ShopifyWebhookValidationFailed,
    WebhookSignatureInvalid,
    WebhookParsingFailed,
    
    // Invoice/order errors
    InvoiceNotFound,
    InvoiceAlreadyProcessed,
    OrderNotEligibleForReward,
    
    // Lightning/Bitcoin errors
    LightningNodeOffline,
    LightningNodeUnreachable,
    InsufficientLightningBalance,
    PullPaymentCreationFailed,
    LnurlGenerationFailed,
    
    // Email/notification errors
    EmailDeliveryFailed,
    EmailConfigurationMissing,
    NotificationServiceDown,
    
    // Balance/wallet errors
    InsufficientBalance,
    WalletNotFound,
    BalanceCalculationError,
    
    // Configuration errors
    PluginNotEnabled,
    StoreNotFound,
    ConfigurationMissing,
    InvalidRewardPercentage,
    
    // Database errors
    DatabaseConnectionFailed,
    DatabaseWriteFailed,
    DatabaseReadFailed,
    MigrationFailed,
    
    // External API errors
    SquareApiError,
    ShopifyApiError,
    ExchangeRateApiFailed,
    ExternalServiceTimeout,
    
    // Business logic errors
    DuplicateReward,
    RewardExpired,
    RewardAlreadyClaimed,
    InvalidRewardAmount
}

/// <summary>
/// Base exception for all Bitcoin Rewards plugin errors
/// </summary>
public class BitcoinRewardsException : Exception
{
    public RewardErrorType ErrorType { get; }
    public string? OrderId { get; set; }
    public string? StoreId { get; set; }
    public string? RewardId { get; set; }
    public Dictionary<string, object> Context { get; }
    
    public BitcoinRewardsException(
        RewardErrorType errorType,
        string message,
        Exception? innerException = null) 
        : base(message, innerException)
    {
        ErrorType = errorType;
        Context = new Dictionary<string, object>();
    }
    
    /// <summary>
    /// Add contextual information to the exception
    /// </summary>
    public BitcoinRewardsException WithContext(string key, object value)
    {
        Context[key] = value;
        return this;
    }
    
    /// <summary>
    /// Set the order ID for this error
    /// </summary>
    public BitcoinRewardsException ForOrder(string orderId)
    {
        OrderId = orderId;
        return this;
    }
    
    /// <summary>
    /// Set the store ID for this error
    /// </summary>
    public BitcoinRewardsException ForStore(string storeId)
    {
        StoreId = storeId;
        return this;
    }
    
    /// <summary>
    /// Set the reward ID for this error
    /// </summary>
    public BitcoinRewardsException ForReward(string rewardId)
    {
        RewardId = rewardId;
        return this;
    }
    
    /// <summary>
    /// Determine if this error is retryable
    /// </summary>
    public bool IsRetryable()
    {
        return ErrorType switch
        {
            // Network/transient errors are retryable
            RewardErrorType.LightningNodeUnreachable => true,
            RewardErrorType.DatabaseConnectionFailed => true,
            RewardErrorType.ExternalServiceTimeout => true,
            RewardErrorType.SquareApiError => true,
            RewardErrorType.ShopifyApiError => true,
            RewardErrorType.ExchangeRateApiFailed => true,
            
            // Configuration/business logic errors are not retryable
            RewardErrorType.PluginNotEnabled => false,
            RewardErrorType.DuplicateReward => false,
            RewardErrorType.RewardAlreadyClaimed => false,
            RewardErrorType.InvalidRewardPercentage => false,
            
            // Default: don't retry unless we know it's safe
            _ => false
        };
    }
    
    /// <summary>
    /// Get user-friendly error message
    /// </summary>
    public string GetUserMessage()
    {
        return ErrorType switch
        {
            RewardErrorType.LightningNodeOffline => "Lightning network is temporarily unavailable. Please try again later.",
            RewardErrorType.InsufficientBalance => "Insufficient balance to create reward. Please contact support.",
            RewardErrorType.EmailDeliveryFailed => "Unable to send email notification. Reward created successfully.",
            RewardErrorType.PluginNotEnabled => "Bitcoin Rewards is not enabled for this store.",
            RewardErrorType.DuplicateReward => "Reward has already been created for this order.",
            _ => "An error occurred processing your reward. Please contact support."
        };
    }
}

/// <summary>
/// Specific exception for webhook validation failures
/// </summary>
public class WebhookValidationException : BitcoinRewardsException
{
    public string? WebhookPayload { get; set; }
    public string? ExpectedSignature { get; set; }
    public string? ReceivedSignature { get; set; }
    
    public WebhookValidationException(string message, Exception? innerException = null)
        : base(RewardErrorType.WebhookSignatureInvalid, message, innerException)
    {
    }
}

/// <summary>
/// Specific exception for Lightning node errors
/// </summary>
public class LightningNodeException : BitcoinRewardsException
{
    public string? NodeAlias { get; set; }
    public string? NodeUri { get; set; }
    
    public LightningNodeException(
        RewardErrorType errorType,
        string message,
        Exception? innerException = null)
        : base(errorType, message, innerException)
    {
    }
}
