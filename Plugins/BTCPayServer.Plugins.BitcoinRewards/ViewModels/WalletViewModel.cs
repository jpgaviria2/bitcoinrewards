using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Plugins.BitcoinRewards.Data.Models;
using BTCPayServer.Plugins.BitcoinRewards.CashuAbstractions;

namespace BTCPayServer.Plugins.BitcoinRewards.ViewModels;

public class WalletViewModel
{
   public List<(string Mint, string Unit, ulong Amount)> AvaibleBalances { get; set; }
   public List<ExportedToken> ExportedTokens { get; set; }

   public IEnumerable<(decimal Amount, string Unit)> GroupedBalances => AvaibleBalances
       .GroupBy(b => b.Unit)
       .OrderByDescending(g => g.Key)
       .Select(gr => CashuUtils.FormatAmount((ulong)gr.Sum(x => (decimal)x.Amount), gr.Key));
}

