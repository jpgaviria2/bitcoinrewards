using BTCPayServer.Data;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using NBXplorer;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.BitcoinRewards
{
    public static class BitcoinRewardsExtensions
    {
        public const string StoreBlobKey = "bitcoinrewards";

        public static BitcoinRewardsSettings? GetBitcoinRewardsSettings(this StoreBlob storeBlob)
        {
            if (storeBlob.AdditionalData.TryGetValue(StoreBlobKey, out var rawS))
            {
                if (rawS is JObject rawObj)
                {
                    return new Serializer(null).ToObject<BitcoinRewardsSettings>(rawObj);
                }
                else if (rawS.Type == JTokenType.String)
                {
                    return new Serializer(null).ToObject<BitcoinRewardsSettings>(rawS.Value<string>());
                }
            }

            return null;
        }

        public static void SetBitcoinRewardsSettings(this StoreBlob storeBlob, BitcoinRewardsSettings settings)
        {
            if (settings is null)
            {
                storeBlob.AdditionalData.Remove(StoreBlobKey);
            }
            else
            {
                var value = new Serializer(null).ToString(settings);
                if (storeBlob.AdditionalData.ContainsKey(StoreBlobKey))
                {
                    storeBlob.AdditionalData[StoreBlobKey] = value;
                }
                else
                {
                    storeBlob.AdditionalData.Add(StoreBlobKey, value);
                }
            }
        }
    }
}

