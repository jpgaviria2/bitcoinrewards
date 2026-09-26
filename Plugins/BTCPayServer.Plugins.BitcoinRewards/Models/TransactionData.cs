#nullable enable
using System;
using System.Collections.Generic;
using BTCPayServer.Data;

namespace BTCPayServer.Plugins.BitcoinRewards.Models;

public enum TransactionPlatform
{
    Square = 0,
    Btcpay = 1
}

public class TransactionData
{
    public string TransactionId { get; set; } = string.Empty;
    public string? OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = StoreBlob.StandardDefaultCurrency;
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerProfileId { get; set; }
    public string? LightningAddressHash { get; set; }
    public string? LightningAddress { get; set; }
    public TransactionPlatform Platform { get; set; }
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Metadata { get; set; } = new();
}
