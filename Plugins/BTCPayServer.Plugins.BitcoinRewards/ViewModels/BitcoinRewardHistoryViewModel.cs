#nullable enable
using System;
using System.Collections.Generic;
using BTCPayServer.Plugins.BitcoinRewards.Data;

namespace BTCPayServer.Plugins.BitcoinRewards.ViewModels;

public class BitcoinRewardHistoryViewModel
{
    public string StoreId { get; set; } = string.Empty;
    
    public List<BitcoinRewardItem> Rewards { get; set; } = new();
    
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    
    public RewardStatus? FilterStatus { get; set; }
    public RewardPlatform? FilterPlatform { get; set; }
    public DateTime? FilterDateFrom { get; set; }
    public DateTime? FilterDateTo { get; set; }
}

public class BitcoinRewardItem
{
    public Guid Id { get; set; }
    public RewardPlatform Platform { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public string? OrderId { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public decimal TransactionAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal RewardAmount { get; set; }
    public long RewardAmountSatoshis { get; set; }
    public RewardStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? RedeemedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? ErrorMessage { get; set; }
    public bool CanReclaim => Status == RewardStatus.Expired || Status == RewardStatus.Pending;
}

