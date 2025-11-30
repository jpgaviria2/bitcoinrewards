using System.Collections.Generic;

namespace BTCPayServer.Plugins.BitcoinRewards.Clients;

public class TransactionsListResp
{
    public List<TransactionDataHolder> transactions { get; set; }
}
