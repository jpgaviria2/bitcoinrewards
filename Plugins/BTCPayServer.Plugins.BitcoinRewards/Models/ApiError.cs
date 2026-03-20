using System.Collections.Generic;

namespace BTCPayServer.Plugins.BitcoinRewards.Models;

/// <summary>
/// Standardized API error response format for consistent error handling across all endpoints.
/// </summary>
public class ApiError
{
    /// <summary>
    /// Human-readable error message
    /// </summary>
    public string Error { get; set; } = string.Empty;
    
    /// <summary>
    /// Machine-readable error code for programmatic handling
    /// </summary>
    public string? Code { get; set; }
    
    /// <summary>
    /// Additional technical details (for debugging)
    /// </summary>
    public string? Detail { get; set; }
    
    /// <summary>
    /// Field-level validation errors (optional)
    /// </summary>
    public Dictionary<string, string>? Fields { get; set; }
    
    /// <summary>
    /// Suggested retry delay in seconds (for rate limiting, temporary failures)
    /// </summary>
    public int? RetryAfterSeconds { get; set; }
}

/// <summary>
/// Standard error codes for Bitcoin Rewards API.
/// Clients should check these codes for programmatic error handling.
/// </summary>
public static class ErrorCodes
{
    // Wallet errors (1000-1999)
    public const string WALLET_NOT_FOUND = "WALLET_NOT_FOUND";
    public const string WALLET_CREATION_FAILED = "WALLET_CREATION_FAILED";
    public const string WALLET_TOKEN_INVALID = "WALLET_TOKEN_INVALID";
    
    // Balance errors (2000-2999)
    public const string INSUFFICIENT_CAD_BALANCE = "INSUFFICIENT_CAD_BALANCE";
    public const string INSUFFICIENT_SATS_BALANCE = "INSUFFICIENT_SATS_BALANCE";
    public const string BALANCE_UPDATE_FAILED = "BALANCE_UPDATE_FAILED";
    public const string BALANCE_INCONSISTENCY = "BALANCE_INCONSISTENCY";
    
    // Payment errors (3000-3999)
    public const string PAYMENT_FAILED = "PAYMENT_FAILED";
    public const string PAYMENT_TIMEOUT = "PAYMENT_TIMEOUT";
    public const string PAYMENT_ROUTING_FAILED = "PAYMENT_ROUTING_FAILED";
    public const string PAYMENT_AMOUNT_INVALID = "PAYMENT_AMOUNT_INVALID";
    public const string PAYMENT_ALREADY_PROCESSED = "PAYMENT_ALREADY_PROCESSED";
    
    // Invoice errors (4000-4999)
    public const string INVOICE_INVALID = "INVOICE_INVALID";
    public const string INVOICE_EXPIRED = "INVOICE_EXPIRED";
    public const string INVOICE_NO_AMOUNT = "INVOICE_NO_AMOUNT";
    public const string INVOICE_DECODE_FAILED = "INVOICE_DECODE_FAILED";
    
    // LNURL errors (5000-5999)
    public const string LNURL_CLAIM_FAILED = "LNURL_CLAIM_FAILED";
    public const string LNURL_CALLBACK_FAILED = "LNURL_CALLBACK_FAILED";
    public const string LNURL_INVALID = "LNURL_INVALID";
    public const string LNURL_ALREADY_CLAIMED = "LNURL_ALREADY_CLAIMED";
    
    // Swap errors (6000-6999)
    public const string SWAP_FAILED = "SWAP_FAILED";
    public const string SWAP_AMOUNT_INVALID = "SWAP_AMOUNT_INVALID";
    public const string SWAP_RATE_UNAVAILABLE = "SWAP_RATE_UNAVAILABLE";
    
    // Rate limiting (7000-7999)
    public const string RATE_LIMIT_EXCEEDED = "RATE_LIMIT_EXCEEDED";
    public const string TOO_MANY_REQUESTS = "TOO_MANY_REQUESTS";
    
    // Exchange rate errors (8000-8999)
    public const string EXCHANGE_RATE_UNAVAILABLE = "EXCHANGE_RATE_UNAVAILABLE";
    public const string EXCHANGE_RATE_STALE = "EXCHANGE_RATE_STALE";
    public const string CONVERSION_ZERO_RESULT = "CONVERSION_ZERO_RESULT";
    
    // System errors (9000-9999)
    public const string DATABASE_ERROR = "DATABASE_ERROR";
    public const string NETWORK_ERROR = "NETWORK_ERROR";
    public const string INTERNAL_ERROR = "INTERNAL_ERROR";
    public const string SERVICE_UNAVAILABLE = "SERVICE_UNAVAILABLE";
    public const string TIMEOUT = "TIMEOUT";
    
    // Validation errors (10000-10999)
    public const string VALIDATION_FAILED = "VALIDATION_FAILED";
    public const string PARAMETER_MISSING = "PARAMETER_MISSING";
    public const string PARAMETER_INVALID = "PARAMETER_INVALID";
    
    // Bolt Card errors (11000-11999)
    public const string CARD_NOT_FOUND = "CARD_NOT_FOUND";
    public const string CARD_DISABLED = "CARD_DISABLED";
    public const string CARD_BALANCE_ERROR = "CARD_BALANCE_ERROR";
    
    // Idempotency (12000-12999)
    public const string DUPLICATE_REQUEST = "DUPLICATE_REQUEST";
    public const string IDEMPOTENCY_MISMATCH = "IDEMPOTENCY_MISMATCH";
}
