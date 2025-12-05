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
    public bool IsAvailable { get; set; }
    public bool IsConfigured { get; set; }
    public string? UnavailableReason { get; set; }
    /// <summary>
    /// Processor ID in format "{Processor}:{PayoutMethodId}" for configured processors
    /// </summary>
    public string ProcessorId { get; set; } = string.Empty;
}

