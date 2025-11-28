#nullable enable
using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Models;

/// <summary>
/// Mint configuration for Bitcoin Rewards plugin's Cashu wallet.
/// Extended to support keyset caching like Cashu plugin.
/// </summary>
public class Mint
{
    public Guid Id { get; set; }
    public string StoreId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Unit { get; set; } = "sat";
    public bool IsActive { get; set; } = true;
    public bool Enabled { get; set; } = true; // Wallet enabled/disabled toggle
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    // Keysets collection for keyset caching (matching Cashu plugin)
    public ICollection<MintKeys> Keysets { get; set; } = new List<MintKeys>();
}

