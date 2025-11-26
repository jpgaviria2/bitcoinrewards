#nullable enable
using System.Collections.Generic;
using BTCPayServer.Payouts;

namespace BTCPayServer.Plugins.BitcoinRewards.ViewModels;

/// <summary>
/// View model for payout processor options in the settings UI
/// </summary>
public class PayoutProcessorOption
{
    public string FactoryName { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public List<PayoutMethodId> SupportedMethods { get; set; } = new();
    public bool IsCashu { get; set; }
    public bool IsAvailable { get; set; }
    public string? UnavailableReason { get; set; }
}

