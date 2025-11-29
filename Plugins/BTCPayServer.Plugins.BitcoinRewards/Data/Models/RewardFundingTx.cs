using System;

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Models;

/// <summary>
/// Optional record linking a reward to its underlying funding transaction.
/// </summary>
public class RewardFundingTx
{
    public Guid Id { get; set; }

    public Guid RewardIssueId { get; set; }

    /// <summary>
    /// Funding source type (Lightning, BTC, Ark, etc.).
    /// </summary>
    public string FundingSource { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the funding transaction (LN invoice id, txid, ark ref, etc.).
    /// </summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>
    /// Raw details JSON blob for debugging.
    /// </summary>
    public string? DetailsJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}


