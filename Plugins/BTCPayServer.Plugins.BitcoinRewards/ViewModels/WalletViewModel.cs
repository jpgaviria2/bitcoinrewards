#nullable enable
namespace BTCPayServer.Plugins.BitcoinRewards.ViewModels;

public class WalletViewModel
{
    public string StoreId { get; set; } = string.Empty;
    public string MintUrl { get; set; } = string.Empty;
    public ulong EcashBalance { get; set; }
    public long LightningBalance { get; set; }
    public string Unit { get; set; } = "sat";
}

