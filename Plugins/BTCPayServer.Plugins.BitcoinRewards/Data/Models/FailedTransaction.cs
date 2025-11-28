using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BTCPayServer.Plugins.BitcoinRewards.CashuAbstractions;

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Models;

/// <summary>
/// FailedTransaction model for tracking failed swap/melt operations (matching Cashu plugin).
/// </summary>
public class FailedTransaction
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }
    
    public required string InvoiceId { get; set; }
    public required string StoreId { get; set; }
    public required string MintUrl { get; set; }
    public required string Unit { get; set; }
    public List<StoredProof> UsedProofs { get; set; } = new();
    
    public required OperationType OperationType { get; set; }
    // For melt operation these will be for fee return. For swap these will contain outputs sent to mint.
    public required CashuUtils.OutputData OutputData { get; set; }
    public MeltDetails? MeltDetails { get; set; }
    public required int RetryCount { get; set; }
    public required DateTimeOffset LastRetried { get; set; }
    public string Details { get; set; } = string.Empty;
    public bool Resolved { get; set; }
}

/// <summary>
/// MeltDetails for tracking melt operation details (matching Cashu plugin).
/// </summary>
public class MeltDetails
{
    public required string MeltQuoteId { get; set; }
    public required DateTimeOffset Expiry { get; set; }
    public required string LightningInvoiceId { get; set; }
    public required string Status { get; set; }
}

/// <summary>
/// OperationType enum for failed transactions (matching Cashu plugin).
/// </summary>
public enum OperationType
{
    Swap,
    Melt
}

