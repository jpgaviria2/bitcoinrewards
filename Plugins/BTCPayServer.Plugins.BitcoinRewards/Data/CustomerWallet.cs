#nullable enable
using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

/// <summary>
/// Dual-balance wallet linked to a bolt card. Tracks both a stable CAD balance
/// (cents in DB, locked at exchange rate when earned) and sats balance (in the
/// associated pull payment).
/// </summary>
public class CustomerWallet
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(50)]
    public string StoreId { get; set; } = string.Empty;

    /// <summary>Pull payment whose Limit holds the sats balance.</summary>
    [Required]
    [MaxLength(100)]
    public string PullPaymentId { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? CardUid { get; set; }

    [MaxLength(100)]
    public string? BoltcardId { get; set; }

    // ── Dual Balances ──

    /// <summary>Stable CAD balance in cents. 150 = CA$1.50. Never fluctuates once credited.</summary>
    [Required]
    public long CadBalanceCents { get; set; } = 0;

    /// <summary>Current sats balance (when AutoConvert is OFF, this tracks earned sats).</summary>
    public long SatsBalanceSatoshis { get; set; } = 0;

    /// <summary>When true, incoming rewards auto-convert to CAD at current rate.</summary>
    public bool AutoConvertToCad { get; set; } = true;

    // ── Tracking ──

    public long TotalRewardedSatoshis { get; set; } = 0;
    public long TotalRewardedCadCents { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastRewardedAt { get; set; }

    /// <summary>SHA-256 hash of bearer token used by PWA for API auth.</summary>
    [MaxLength(128)]
    public string? ApiTokenHash { get; set; }
}
