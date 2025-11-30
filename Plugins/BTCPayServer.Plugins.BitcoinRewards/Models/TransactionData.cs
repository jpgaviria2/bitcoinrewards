#nullable enable
using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.BitcoinRewards.Models;

public enum TransactionPlatform
{
    Shopify = 0,
    Square = 1
}

public class TransactionData
{
    public string TransactionId { get; set; } = string.Empty;
    public string? OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public TransactionPlatform Platform { get; set; }
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Metadata { get; set; } = new();
}

