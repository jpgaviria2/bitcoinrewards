using System;
using DotNut;

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Models;

/// <summary>
/// MintKeys model for caching keysets per mint (matching Cashu plugin).
/// Note: Uses Guid for MintId to match our Mint model (Cashu uses int).
/// </summary>
public record MintKeys
{
    public Guid MintId { get; set; }
    public Mint Mint { get; set; } = null!;
    public KeysetId KeysetId { get; set; }
    
    public string Unit { get; set; } = string.Empty;
    public Keyset Keyset { get; set; } = null!;
}

