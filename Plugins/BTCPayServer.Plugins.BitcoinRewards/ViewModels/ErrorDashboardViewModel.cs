using System.Collections.Generic;
using BTCPayServer.Plugins.BitcoinRewards.Models;

namespace BTCPayServer.Plugins.BitcoinRewards.ViewModels
{
    public class ErrorDashboardViewModel
    {
        public string StoreId { get; set; } = string.Empty;
        public List<RewardError> Errors { get; set; } = new();
        public ErrorStatistics Statistics { get; set; } = new();
        public int DaysFilter { get; set; } = 7;
        public bool? ResolvedFilter { get; set; }
    }
    
    public class ErrorStatistics
    {
        public int TotalErrors { get; set; }
        public int UnresolvedErrors { get; set; }
        public int RetryableErrors { get; set; }
        public Dictionary<string, int> ErrorsByType { get; set; } = new();
        public Dictionary<string, int> ErrorsByOperation { get; set; } = new();
    }
}
