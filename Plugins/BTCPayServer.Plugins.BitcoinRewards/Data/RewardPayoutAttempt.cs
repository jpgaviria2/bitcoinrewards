#nullable enable
using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

public enum RewardPayoutState
{
    Queued = 0,
    Resolving = 1,
    InvoiceCreated = 2,
    Paying = 3,
    Paid = 4,
    RetryableFailure = 5,
    PermanentFailure = 6,
    ManualReview = 7
}

/// <summary>
/// Durable direct-payout saga state. Part B creates the schema before dispatch is enabled so
/// no external payment can occur without an idempotent record and a reconciliation trail.
/// </summary>
public class RewardPayoutAttempt
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RewardId { get; set; }
    public int AttemptNumber { get; set; }
    [Required, MaxLength(64)] public string LightningAddressHash { get; set; } = string.Empty;
    public RewardPayoutState State { get; set; } = RewardPayoutState.Queued;
    public long AmountSatoshis { get; set; }
    [MaxLength(64)] public string? PaymentHash { get; set; }
    [MaxLength(255)] public string? ProviderReference { get; set; }
    [MaxLength(1000)] public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? NextRetryAt { get; set; }
    public DateTime? PaidAt { get; set; }
}
