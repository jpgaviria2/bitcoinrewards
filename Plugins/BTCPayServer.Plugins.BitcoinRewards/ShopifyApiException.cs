using System;

namespace BTCPayServer.Plugins.BitcoinRewards
{
    public class ShopifyApiException : Exception
    {
        public ShopifyApiException(string message) : base(message)
        {
        }
    }
}
