#nullable enable
using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

/// <summary>
/// Standalone NIP-05 identity entry for users without wallets.
/// Used for pre-seeded community members (manager, jp, birchy, etc.)
/// </summary>
public class Nip05Identity
{
    [Key]
    public int Id { get; set; }

    /// <summary>Nostr public key (hex format).</summary>
    [Required]
    [MaxLength(64)]
    public string Pubkey { get; set; } = string.Empty;

    /// <summary>NIP-05 username (becomes username@trailscoffee.com).</summary>
    [Required]
    [MaxLength(20)]
    public string Username { get; set; } = string.Empty;

    /// <summary>If true, user is hidden from nostr.json (moderation).</summary>
    public bool Revoked { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
