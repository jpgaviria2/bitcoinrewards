#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Plugins.BitcoinRewards.Data;

namespace BTCPayServer.Plugins.BitcoinRewards.ViewModels;

public class CustomerWalletListViewModel
{
    public string StoreId { get; set; } = string.Empty;
    public List<CustomerWalletListItem> Wallets { get; set; } = new();
}

public class CustomerWalletListItem
{
    public Guid Id { get; set; }
    public string? CardUid { get; set; }
    public long CadBalanceCents { get; set; }
    public long SatsBalance { get; set; }
    public bool AutoConvertToCad { get; set; }
    public long TotalRewardedCadCents { get; set; }
    public long TotalRewardedSatoshis { get; set; }
    public DateTime? LastRewardedAt { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    public string CadBalanceFormatted => $"CA${CadBalanceCents / 100m:F2}";
    public string TotalRewardedCadFormatted => $"CA${TotalRewardedCadCents / 100m:F2}";
    public string CardUidDisplay => CardUid ?? "Unlinked";
}

public class CustomerWalletDetailViewModel
{
    public string StoreId { get; set; } = string.Empty;
    public Guid WalletId { get; set; }
    public string? CardUid { get; set; }
    public string? BoltcardId { get; set; }
    public long CadBalanceCents { get; set; }
    public long SatsBalance { get; set; }
    public bool AutoConvertToCad { get; set; }
    public long TotalRewardedSatoshis { get; set; }
    public long TotalRewardedCadCents { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastRewardedAt { get; set; }
    public string? ApiTokenHash { get; set; }
    public string PullPaymentId { get; set; } = string.Empty;
    public List<WalletTransactionItem> Transactions { get; set; } = new();

    public string CadBalanceFormatted => $"CA${CadBalanceCents / 100m:F2}";
    public string TotalRewardedCadFormatted => $"CA${TotalRewardedCadCents / 100m:F2}";
    public string CardUidDisplay => CardUid ?? "Unlinked";
    public string TokenMasked => string.IsNullOrEmpty(ApiTokenHash) ? "None" : $"...{ApiTokenHash[^8..]}";
}

public class WalletTransactionItem
{
    public Guid Id { get; set; }
    public WalletTransactionType Type { get; set; }
    public long SatsAmount { get; set; }
    public long CadCentsAmount { get; set; }
    public decimal ExchangeRate { get; set; }
    public string? Reference { get; set; }
    public DateTime CreatedAt { get; set; }

    public string CadFormatted => $"CA${CadCentsAmount / 100m:F2}";

    public string TypeBadgeClass => Type switch
    {
        WalletTransactionType.RewardEarned => "bg-success",
        WalletTransactionType.SwapToCad => "bg-info",
        WalletTransactionType.SwapToSats => "bg-info",
        WalletTransactionType.CadSpent => "bg-warning text-dark",
        WalletTransactionType.SatsWithdrawn => "bg-warning text-dark",
        WalletTransactionType.ManualAdjust => "bg-secondary",
        _ => "bg-secondary"
    };

    public string TypeLabel => Type switch
    {
        WalletTransactionType.RewardEarned => "Earned",
        WalletTransactionType.SwapToCad => "Swap→CAD",
        WalletTransactionType.SwapToSats => "Swap→Sats",
        WalletTransactionType.CadSpent => "Spent",
        WalletTransactionType.SatsWithdrawn => "Withdrawn",
        WalletTransactionType.ManualAdjust => "Adjusted",
        _ => "Unknown"
    };
}

public class AdjustBalanceViewModel
{
    public string StoreId { get; set; } = string.Empty;
    public Guid WalletId { get; set; }
    public string? CardUid { get; set; }
    public long CurrentCadBalanceCents { get; set; }
    public long CurrentSatsBalance { get; set; }

    [Required]
    [Display(Name = "Balance Type")]
    public string BalanceType { get; set; } = "cad"; // "cad" or "sats"

    [Required]
    [Range(1, long.MaxValue, ErrorMessage = "Amount must be positive")]
    [Display(Name = "Amount")]
    public long Amount { get; set; }

    [Required]
    [Display(Name = "Direction")]
    public string Direction { get; set; } = "credit"; // "credit" or "debit"

    [Display(Name = "Reason")]
    [MaxLength(255)]
    public string? Reason { get; set; }

    public string CurrentCadFormatted => $"CA${CurrentCadBalanceCents / 100m:F2}";
}

public class SpendCadViewModel
{
    public string StoreId { get; set; } = string.Empty;
    public Guid WalletId { get; set; }
    public string? CardUid { get; set; }
    public long CurrentCadBalanceCents { get; set; }

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be positive")]
    [Display(Name = "Amount (CAD)")]
    public decimal AmountCad { get; set; }

    [Display(Name = "Reference")]
    [MaxLength(255)]
    public string? Reference { get; set; }

    public string CurrentCadFormatted => $"CA${CurrentCadBalanceCents / 100m:F2}";
}
