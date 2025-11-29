using System.Collections.Generic;
using BTCPayServer.Plugins.BitcoinRewards.Data.enums;

namespace BTCPayServer.Plugins.BitcoinRewards.PaymentHandlers;

public class WalletPaymentMethodConfig
{
    public CashuPaymentModel PaymentModel { get; set; }
    
    public List<string> TrustedMintsUrls { get; set; }
    
    public WalletFeeConfig FeeConfing { get; set; }
}

public class WalletFeeConfig
{
    //in %
    public int MaxKeysetFee { get; set; }
    
    //in %
    public int MaxLightningFee { get; set; }
    
    //in sats - estimated fee that user pays for us in order to cover fee expenses. Added as Tweak Fee
    public int CustomerFeeAdvance { get; set; }
}

