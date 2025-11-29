using System;

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Models;

/// <summary>
/// Represents a single ecash reward issued to a customer.
/// </summary>
public class RewardIssue
{
    public Guid Id { get; set; }
    public string StoreId { get; set; } = string.Empty;

    /// <summary>
    /// External order/invoice id this reward is attached to (Shopify, Square, etc.).
    /// </summary>
    public string? OrderId { get; set; }

    public string? InvoiceId { get; set; }

    public string CustomerEmail { get; set; } = string.Empty;

    /// <summary>
    /// Reward amount in sats.
    /// </summary>
    public long AmountSats { get; set; }

    /// <summary>
    /// Encoded Cashu token string that was sent to the user.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Simple status flag: Pending, Sent, Claimed, Expired, Failed.
    /// </summary>
    public string Status { get; set; } = "Pending";

    /// <summary>
    /// Optional error information if issuance failed.
    /// </summary>
    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public DateTime? ClaimedAt { get; set; }
}


