using System;

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Models;

/// <summary>
/// Per-store configuration for the Bitcoin Rewards CDK-based flow.
/// </summary>
public class RewardsConfig
{
    public Guid Id { get; set; }
    public string StoreId { get; set; } = string.Empty;

    /// <summary>
    /// Funding source type (e.g. "Lightning", "BTC", "Ark").
    /// </summary>
    public string FundingSource { get; set; } = "Lightning";

    /// <summary>
    /// Percentage of order total to issue as rewards (0-100).
    /// </summary>
    public decimal RewardsPercentage { get; set; } = 0m;

    /// <summary>
    /// Optional maximum reward per order in sats.
    /// </summary>
    public long? MaxRewardSats { get; set; }

    /// <summary>
    /// Cashu mint URL used to mint ecash tokens.
    /// </summary>
    public string MintUrl { get; set; } = string.Empty;

    /// <summary>
    /// Cashu unit, typically \"sat\".
    /// </summary>
    public string Unit { get; set; } = "sat";

    /// <summary>
    /// Serialized email subject/body templates (simple string for now).
    /// </summary>
    public string EmailSubjectTemplate { get; set; } = string.Empty;
    public string EmailBodyTemplate { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}


