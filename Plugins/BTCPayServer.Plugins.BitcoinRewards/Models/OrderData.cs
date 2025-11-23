namespace BTCPayServer.Plugins.BitcoinRewards.Models
{
    public class OrderData
    {
        public string OrderId { get; set; } = null!;
        public string OrderNumber { get; set; } = null!;
        public decimal OrderAmount { get; set; }
        public string Currency { get; set; } = null!;
        public string CustomerEmail { get; set; } = null!;
        public string CustomerPhone { get; set; } = null!;
        public string CustomerName { get; set; } = null!;
        public string Source { get; set; } = null!; // "shopify"
        public string StoreId { get; set; } = null!;
    }
}

