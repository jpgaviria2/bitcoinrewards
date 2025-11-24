using Newtonsoft.Json;

namespace BTCPayServer.Plugins.BitcoinRewards.Clients;

public class CountResponse
{
    [JsonProperty("count")]
    public long Count { get; set; }
}
