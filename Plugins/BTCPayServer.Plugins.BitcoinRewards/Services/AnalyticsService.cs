using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services
{
    /// <summary>
    /// Analytics service for generating dashboard metrics and insights
    /// </summary>
    public class AnalyticsService
    {
        private readonly BitcoinRewardsPluginDbContextFactory _contextFactory;
        private readonly ILogger<AnalyticsService> _logger;
        
        public AnalyticsService(
            BitcoinRewardsPluginDbContextFactory contextFactory,
            ILogger<AnalyticsService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }
        
        /// <summary>
        /// Generate complete analytics dashboard data
        /// </summary>
        public async Task<AnalyticsDashboard> GenerateDashboardAsync(
            string storeId,
            DateTime startDate,
            DateTime endDate)
        {
            await using var context = _contextFactory.CreateContext();
            
            var rewards = await context.BitcoinRewardRecords
                .Where(r => r.StoreId == storeId && 
                           r.CreatedAt >= startDate && 
                           r.CreatedAt <= endDate)
                .ToListAsync();
            
            var dashboard = new AnalyticsDashboard
            {
                StoreId = storeId,
                StartDate = startDate,
                EndDate = endDate
            };
            
            // Generate overview metrics
            dashboard.Overview = GenerateOverview(rewards, startDate, endDate);
            
            // Generate time series
            dashboard.RewardsCreated = GenerateTimeSeriesCreated(rewards);
            dashboard.RewardsClaimed = GenerateTimeSeriesClaimed(rewards);
            dashboard.RevenueImpact = GenerateTimeSeriesRevenue(rewards);
            
            // Generate breakdowns
            dashboard.ByPlatform = GeneratePlatformBreakdown(rewards);
            dashboard.ByStatus = GenerateStatusBreakdown(rewards);
            dashboard.ByHourOfDay = GenerateHourOfDayBreakdown(rewards);
            
            // Generate top performers
            dashboard.TopRewards = GenerateTopRewards(rewards);
            dashboard.CustomerInsights = GenerateCustomerInsights(rewards);
            
            return dashboard;
        }
        
        private AnalyticsOverview GenerateOverview(
            List<BitcoinRewardRecord> rewards,
            DateTime startDate,
            DateTime endDate)
        {
            var claimed = rewards.Where(r => r.Status == RewardStatus.Redeemed).ToList();
            
            var overview = new AnalyticsOverview
            {
                TotalRewardsCreated = rewards.Count,
                TotalRewardsClaimed = claimed.Count,
                ClaimRate = rewards.Count > 0 
                    ? (decimal)claimed.Count / rewards.Count * 100 
                    : 0,
                
                TotalRewardedSatoshis = rewards.Sum(r => r.RewardAmountSatoshis),
                TotalClaimedSatoshis = claimed.Sum(r => r.RewardAmountSatoshis),
                UnclaimedSatoshis = rewards
                    .Where(r => r.Status != RewardStatus.Redeemed)
                    .Sum(r => r.RewardAmountSatoshis),
                AverageRewardSatoshis = rewards.Count > 0
                    ? rewards.Average(r => r.RewardAmountSatoshis)
                    : 0,
                
                TotalTransactionAmount = rewards.Sum(r => r.TransactionAmount),
                TransactionCurrency = rewards.FirstOrDefault()?.Currency ?? "USD",
                TotalTransactions = rewards.Count,
                AverageTransactionAmount = rewards.Count > 0
                    ? rewards.Average(r => r.TransactionAmount)
                    : 0
            };
            
            // Calculate claim time metrics
            var claimTimes = claimed
                .Where(r => r.ClaimedAt.HasValue)
                .Select(r => (r.ClaimedAt!.Value - r.CreatedAt).TotalMinutes)
                .OrderBy(t => t)
                .ToList();
            
            if (claimTimes.Any())
            {
                overview.AverageClaimTimeMinutes = claimTimes.Average();
                overview.MedianClaimTimeMinutes = claimTimes[claimTimes.Count / 2];
            }
            
            // Calculate comparison with previous period
            var periodLength = endDate - startDate;
            var previousStart = startDate - periodLength;
            var previousEnd = startDate;
            
            overview.Comparison = CalculateComparison(
                rewards,
                previousStart,
                previousEnd);
            
            return overview;
        }
        
        private AnalyticsComparison CalculateComparison(
            List<BitcoinRewardRecord> currentRewards,
            DateTime previousStart,
            DateTime previousEnd)
        {
            // This would need to fetch previous period data
            // For now, return empty comparison
            return new AnalyticsComparison
            {
                RewardsCreatedChange = 0,
                ClaimRateChange = 0,
                ValueChange = 0,
                TransactionCountChange = 0
            };
        }
        
        private List<TimeSeriesDataPoint> GenerateTimeSeriesCreated(
            List<BitcoinRewardRecord> rewards)
        {
            return rewards
                .GroupBy(r => r.CreatedAt.Date)
                .OrderBy(g => g.Key)
                .Select(g => new TimeSeriesDataPoint
                {
                    Timestamp = g.Key,
                    Count = g.Count(),
                    Value = g.Sum(r => r.RewardAmountSatoshis)
                })
                .ToList();
        }
        
        private List<TimeSeriesDataPoint> GenerateTimeSeriesClaimed(
            List<BitcoinRewardRecord> rewards)
        {
            return rewards
                .Where(r => r.ClaimedAt.HasValue)
                .GroupBy(r => r.ClaimedAt!.Value.Date)
                .OrderBy(g => g.Key)
                .Select(g => new TimeSeriesDataPoint
                {
                    Timestamp = g.Key,
                    Count = g.Count(),
                    Value = g.Sum(r => r.RewardAmountSatoshis)
                })
                .ToList();
        }
        
        private List<TimeSeriesDataPoint> GenerateTimeSeriesRevenue(
            List<BitcoinRewardRecord> rewards)
        {
            return rewards
                .GroupBy(r => r.CreatedAt.Date)
                .OrderBy(g => g.Key)
                .Select(g => new TimeSeriesDataPoint
                {
                    Timestamp = g.Key,
                    Count = g.Count(),
                    Value = g.Sum(r => r.TransactionAmount)
                })
                .ToList();
        }
        
        private List<CategoryBreakdown> GeneratePlatformBreakdown(
            List<BitcoinRewardRecord> rewards)
        {
            var total = rewards.Count;
            return rewards
                .GroupBy(r => r.Platform)
                .Select(g => new CategoryBreakdown
                {
                    Category = g.Key.ToString(),
                    Count = g.Count(),
                    Percentage = total > 0 ? (decimal)g.Count() / total * 100 : 0,
                    TotalSatoshis = g.Sum(r => r.RewardAmountSatoshis),
                    AverageSatoshis = (decimal)g.Average(r => r.RewardAmountSatoshis)
                })
                .OrderByDescending(b => b.Count)
                .ToList();
        }
        
        private List<CategoryBreakdown> GenerateStatusBreakdown(
            List<BitcoinRewardRecord> rewards)
        {
            var total = rewards.Count;
            return rewards
                .GroupBy(r => r.Status)
                .Select(g => new CategoryBreakdown
                {
                    Category = g.Key.ToString(),
                    Count = g.Count(),
                    Percentage = total > 0 ? (decimal)g.Count() / total * 100 : 0,
                    TotalSatoshis = g.Sum(r => r.RewardAmountSatoshis),
                    AverageSatoshis = (decimal)g.Average(r => r.RewardAmountSatoshis)
                })
                .OrderByDescending(b => b.Count)
                .ToList();
        }
        
        private List<CategoryBreakdown> GenerateHourOfDayBreakdown(
            List<BitcoinRewardRecord> rewards)
        {
            var total = rewards.Count;
            return rewards
                .GroupBy(r => r.CreatedAt.Hour)
                .Select(g => new CategoryBreakdown
                {
                    Category = $"{g.Key:D2}:00",
                    Count = g.Count(),
                    Percentage = total > 0 ? (decimal)g.Count() / total * 100 : 0,
                    TotalSatoshis = g.Sum(r => r.RewardAmountSatoshis),
                    AverageSatoshis = (decimal)g.Average(r => r.RewardAmountSatoshis)
                })
                .OrderBy(b => b.Category)
                .ToList();
        }
        
        private List<TopItem> GenerateTopRewards(List<BitcoinRewardRecord> rewards)
        {
            return rewards
                .OrderByDescending(r => r.RewardAmountSatoshis)
                .Take(10)
                .Select(r => new TopItem
                {
                    Id = r.Id.ToString(),
                    Description = $"{r.TransactionAmount:C} → {r.RewardAmountSatoshis} sats",
                    AmountSatoshis = r.RewardAmountSatoshis,
                    CreatedAt = r.CreatedAt,
                    Status = r.Status.ToString()
                })
                .ToList();
        }
        
        private List<CustomerInsight> GenerateCustomerInsights(
            List<BitcoinRewardRecord> rewards)
        {
            return rewards
                .Where(r => !string.IsNullOrEmpty(r.CustomerEmail))
                .GroupBy(r => r.CustomerEmail)
                .Select(g =>
                {
                    var customerRewards = g.ToList();
                    var claimed = customerRewards.Count(r => r.Status == RewardStatus.Redeemed);
                    
                    return new CustomerInsight
                    {
                        CustomerEmailHash = HashEmail(g.Key!),
                        TotalRewards = customerRewards.Count,
                        ClaimedRewards = claimed,
                        TotalSatoshis = customerRewards.Sum(r => r.RewardAmountSatoshis),
                        FirstRewardDate = customerRewards.Min(r => r.CreatedAt),
                        LastRewardDate = customerRewards.Max(r => r.CreatedAt),
                        ClaimRate = customerRewards.Count > 0
                            ? (decimal)claimed / customerRewards.Count * 100
                            : 0
                    };
                })
                .OrderByDescending(c => c.TotalSatoshis)
                .Take(20)
                .ToList();
        }
        
        /// <summary>
        /// Hash email for privacy (SHA256)
        /// </summary>
        private static string HashEmail(string email)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(email.ToLowerInvariant());
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToHexString(hash)[..16]; // First 16 chars for display
        }
        
        /// <summary>
        /// Export analytics data to specified format
        /// </summary>
        public async Task<byte[]> ExportDataAsync(ExportRequest request)
        {
            await using var context = _contextFactory.CreateContext();
            
            var rewards = await context.BitcoinRewardRecords
                .Where(r => r.StoreId == request.StoreId &&
                           r.CreatedAt >= request.StartDate &&
                           r.CreatedAt <= request.EndDate)
                .ToListAsync();
            
            return request.Format switch
            {
                ExportFormat.CSV => ExportToCsv(rewards, request),
                ExportFormat.JSON => ExportToJson(rewards, request),
                ExportFormat.Excel => ExportToExcel(rewards, request),
                _ => throw new ArgumentException($"Unknown export format: {request.Format}")
            };
        }
        
        private byte[] ExportToCsv(List<BitcoinRewardRecord> rewards, ExportRequest request)
        {
            var csv = new StringBuilder();
            
            // Header
            csv.AppendLine("Date,TransactionId,Platform,Status,TransactionAmount,Currency,RewardSatoshis,ClaimedAt");
            
            // Rows
            foreach (var reward in rewards)
            {
                csv.AppendLine($"{reward.CreatedAt:yyyy-MM-dd HH:mm:ss}," +
                              $"{reward.TransactionId}," +
                              $"{reward.Platform}," +
                              $"{reward.Status}," +
                              $"{reward.TransactionAmount}," +
                              $"{reward.Currency}," +
                              $"{reward.RewardAmountSatoshis}," +
                              $"{reward.ClaimedAt?.ToString("yyyy-MM-dd HH:mm:ss")}");
            }
            
            return Encoding.UTF8.GetBytes(csv.ToString());
        }
        
        private byte[] ExportToJson(List<BitcoinRewardRecord> rewards, ExportRequest request)
        {
            var data = rewards.Select(r => new
            {
                date = r.CreatedAt,
                transactionId = r.TransactionId,
                platform = r.Platform.ToString(),
                status = r.Status.ToString(),
                transactionAmount = r.TransactionAmount,
                currency = r.Currency,
                rewardSatoshis = r.RewardAmountSatoshis,
                claimedAt = r.ClaimedAt
            });
            
            var json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            return Encoding.UTF8.GetBytes(json);
        }
        
        private byte[] ExportToExcel(List<BitcoinRewardRecord> rewards, ExportRequest request)
        {
            // Excel export would require a library like EPPlus or ClosedXML
            // For now, return CSV format
            _logger.LogWarning("Excel export not yet implemented, returning CSV format");
            return ExportToCsv(rewards, request);
        }
    }
}
