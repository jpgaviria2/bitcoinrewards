#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

/// <summary>
/// Links a physical NTAG 424 DNA bolt card to a pull payment for reward accumulation.
/// Cards are anonymous — no customer linking. The card UID maps to a pull payment
/// whose limit is increased each time a reward is collected via NFC tap.
/// </summary>
public class BoltCardLink
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Store that issued this card.</summary>
    [Required]
    [MaxLength(50)]
    public string StoreId { get; set; } = string.Empty;

    /// <summary>
    /// The pull payment whose Limit is topped up when rewards are collected.
    /// Unique per store — one pull payment per card.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string PullPaymentId { get; set; } = string.Empty;

    /// <summary>
    /// Hex-encoded card UID from the NTAG 424 DNA chip (7 bytes → 14 hex chars).
    /// Nullable until the card is first tapped (factory may pre-create records).
    /// </summary>
    [MaxLength(50)]
    public string? CardUid { get; set; }

    /// <summary>
    /// The boltcard registration ID (hex of issuerKey.GetId(uid)).
    /// Used to look up the card in the core boltcards table.
    /// </summary>
    [MaxLength(100)]
    public string? BoltcardId { get; set; }

    /// <summary>Whether this card link is active and can collect rewards.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Running total of satoshis rewarded to this card.</summary>
    public long TotalRewardedSatoshis { get; set; } = 0;

    /// <summary>Last time a reward was collected on this card.</summary>
    public DateTime? LastRewardedAt { get; set; }
}
