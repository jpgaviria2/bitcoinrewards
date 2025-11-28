#nullable enable
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Plugins.BitcoinRewards.Data.Models;

namespace BTCPayServer.Plugins.BitcoinRewards.ViewModels;

public class WalletViewModel
{
    public string StoreId { get; set; } = string.Empty;
    public string MintUrl { get; set; } = string.Empty;
    public ulong EcashBalance { get; set; }
    public long LightningBalance { get; set; }
    public string Unit { get; set; } = "sat";
    
    public List<(string Mint, string Unit, ulong Amount)> AvailableBalances { get; set; } = new();
    public List<ExportedToken> ExportedTokens { get; set; } = new();
    
    public IEnumerable<(decimal Amount, string Unit)> GroupedBalances => AvailableBalances
        .GroupBy(b => b.Unit)
        .OrderByDescending(g => g.Key)
        .Select(gr => ((decimal)gr.Sum(x => (decimal)x.Amount), gr.Key));
}

