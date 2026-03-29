using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.BitcoinRewards.Models
{
    /// <summary>
    /// Analytics data for dashboard visualization
    /// </summary>
    public class AnalyticsDashboard
    {
        public string StoreId { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        
        // Overview metrics
        public AnalyticsOverview Overview { get; set; } = new();
        
        // Time series data
        public List<TimeSeriesDataPoint> RewardsCreated { get; set; } = new();
        public List<TimeSeriesDataPoint> RewardsClaimed { get; set; } = new();
        public List<TimeSeriesDataPoint> RevenueImpact { get; set; } = new();
        
        // Breakdown data
        public List<CategoryBreakdown> ByPlatform { get; set; } = new();
        public List<CategoryBreakdown> ByStatus { get; set; } = new();
        public List<CategoryBreakdown> ByHourOfDay { get; set; } = new();
        
        // Top performers
        public List<TopItem> TopRewards { get; set; } = new();
        public List<CustomerInsight> CustomerInsights { get; set; } = new();
    }
    
    public class AnalyticsOverview
    {
        // Reward metrics
        public int TotalRewardsCreated { get; set; }
        public int TotalRewardsClaimed { get; set; }
        public decimal ClaimRate { get; set; } // Percentage
        
        // Value metrics
        public long TotalRewardedSatoshis { get; set; }
        public long TotalClaimedSatoshis { get; set; }
        public long UnclaimedSatoshis { get; set; }
        public decimal AverageRewardSatoshis { get; set; }
        
        // Transaction metrics
        public decimal TotalTransactionAmount { get; set; }
        public string TransactionCurrency { get; set; } = "USD";
        public int TotalTransactions { get; set; }
        public decimal AverageTransactionAmount { get; set; }
        
        // Timing metrics
        public double AverageClaimTimeMinutes { get; set; }
        public double MedianClaimTimeMinutes { get; set; }
        
        // Comparison with previous period
        public AnalyticsComparison Comparison { get; set; } = new();
    }
    
    public class AnalyticsComparison
    {
        public decimal RewardsCreatedChange { get; set; } // Percentage change
        public decimal ClaimRateChange { get; set; }
        public decimal ValueChange { get; set; }
        public decimal TransactionCountChange { get; set; }
    }
    
    public class TimeSeriesDataPoint
    {
        public DateTime Timestamp { get; set; }
        public decimal Value { get; set; }
        public int Count { get; set; }
    }
    
    public class CategoryBreakdown
    {
        public string Category { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal Percentage { get; set; }
        public long TotalSatoshis { get; set; }
        public decimal AverageSatoshis { get; set; }
    }
    
    public class TopItem
    {
        public string Id { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public long AmountSatoshis { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; } = string.Empty;
    }
    
    public class CustomerInsight
    {
        public string CustomerEmailHash { get; set; } = string.Empty; // Hashed for privacy
        public int TotalRewards { get; set; }
        public int ClaimedRewards { get; set; }
        public long TotalSatoshis { get; set; }
        public DateTime FirstRewardDate { get; set; }
        public DateTime LastRewardDate { get; set; }
        public decimal ClaimRate { get; set; }
    }
    
    /// <summary>
    /// Export format options
    /// </summary>
    public enum ExportFormat
    {
        CSV,
        JSON,
        Excel
    }
    
    /// <summary>
    /// Export data request
    /// </summary>
    public class ExportRequest
    {
        public string StoreId { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public ExportFormat Format { get; set; } = ExportFormat.CSV;
        public bool IncludeCustomerData { get; set; } = false;
        public bool IncludeTransactionDetails { get; set; } = true;
    }
}
