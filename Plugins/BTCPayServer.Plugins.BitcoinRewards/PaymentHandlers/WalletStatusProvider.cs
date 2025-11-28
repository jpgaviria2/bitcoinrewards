using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Plugins.BitcoinRewards;
using BTCPayServer.Plugins.Cashu.Data.enums;
using BTCPayServer.Services.Stores;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.BitcoinRewards.PaymentHandlers;

public class WalletStatusProvider(StoreRepository storeRepository)
{
    public async Task<bool> WalletEnabled(string storeId)
    {
        try
        {
            var storeData = await storeRepository.FindStore(storeId);

            // Get config directly from JToken since we don't have a registered payment handler
            var configToken = storeData?.GetPaymentMethodConfig(BitcoinRewardsPlugin.WalletPmid);
            var currentPaymentMethodConfig = configToken?.ToObject<WalletPaymentMethodConfig>();
            
            if (currentPaymentMethodConfig == null)
                return false;
            
            if (currentPaymentMethodConfig.PaymentModel == CashuPaymentModel.MeltImmediately)
            {
                if (!storeData.IsLightningEnabled("BTC"))
                {
                    return false;
                }
            }
            
            var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();
            var enabled = !excludeFilters.Match(BitcoinRewardsPlugin.WalletPmid);

            return enabled;
        }
        catch
        {
            return false;
        }
    }
}

