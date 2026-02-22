#nullable enable
using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

public enum WalletTransactionType
{
    RewardEarned = 0,
    SwapToCad = 1,
    SwapToSats = 2,
    CadSpent = 3,
    SatsWithdrawn = 4,
    ManualAdjust = 5
}

/// <summary>
/// Audit log of every balance change on a CustomerWallet.
/// </summary>
public class WalletTransaction
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid CustomerWalletId { get; set; }

    [Required]
    public WalletTransactionType Type { get; set; }

    /// <summary>Amount in sats (positive = credit, negative = debit).</summary>
    public long SatsAmount { get; set; } = 0;

    /// <summary>Amount in CAD cents (positive = credit, negative = debit).</summary>
    public long CadCentsAmount { get; set; } = 0;

    /// <summary>BTC/CAD exchange rate at time of transaction (sats per 1 CAD).</summary>
    public decimal ExchangeRate { get; set; }

    /// <summary>Reference (reward ID, swap ID, invoice ID, etc.).</summary>
    [MaxLength(255)]
    public string? Reference { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
