namespace BTCPayServer.Plugins.BitcoinRewards.Models
{
    public class OrderData
    {
        public string OrderId { get; set; }
        public string OrderNumber { get; set; }
        public decimal OrderAmount { get; set; }
        public string Currency { get; set; }
        public string CustomerEmail { get; set; }
        public string CustomerPhone { get; set; }
        public string CustomerName { get; set; }
        public string Source { get; set; } // "shopify" or "square"
        public string StoreId { get; set; }
    }
}

