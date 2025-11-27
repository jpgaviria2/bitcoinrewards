#nullable enable
using System;

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Models;

/// <summary>
/// Mint configuration for Bitcoin Rewards plugin's Cashu wallet.
/// </summary>
public class Mint
{
    public Guid Id { get; set; }
    public string StoreId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Unit { get; set; } = "sat";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

