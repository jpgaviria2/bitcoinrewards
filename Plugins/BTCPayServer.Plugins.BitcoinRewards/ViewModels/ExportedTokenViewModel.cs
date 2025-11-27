#nullable enable
namespace BTCPayServer.Plugins.BitcoinRewards.ViewModels;

public class ExportedTokenViewModel
{
    public string Token { get; set; } = string.Empty;
    public ulong Amount { get; set; }
    public string FormatedAmount { get; set; } = string.Empty;
    public string MintAddress { get; set; } = string.Empty;
    public string Unit { get; set; } = "sat";
}

