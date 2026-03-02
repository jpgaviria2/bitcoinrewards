#nullable enable
using System;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

public class PendingLnurlClaim
{
    public Guid Id { get; set; }
    public Guid CustomerWalletId { get; set; }
    public string StoreId { get; set; } = string.Empty;
    public string LightningInvoiceId { get; set; } = string.Empty;
    public string Bolt11 { get; set; } = string.Empty;
    public long ExpectedSats { get; set; }
    public string? K1Prefix { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsFailed { get; set; }
}
