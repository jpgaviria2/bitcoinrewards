using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Models;

/// <summary>
/// ExportedToken model for tracking exported tokens (matching Cashu plugin).
/// </summary>
public record ExportedToken
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }
    
    public string SerializedToken { get; set; } = string.Empty;
    public ulong Amount { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Mint { get; set; } = string.Empty;
    public bool IsUsed { get; set; }
    
    public DateTimeOffset CreatedAt { get; set; } = DateTime.UtcNow;
    public string StoreId { get; set; } = string.Empty;
}

