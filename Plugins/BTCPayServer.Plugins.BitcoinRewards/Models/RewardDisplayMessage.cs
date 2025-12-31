#nullable enable
using System;

namespace BTCPayServer.Plugins.BitcoinRewards.Models;

/// <summary>
/// Message sent via SignalR to display devices when a reward is created without email/phone
/// </summary>
public class RewardDisplayMessage
{
    /// <summary>
    /// The pull payment claim URL that can be converted to LNURL
    /// </summary>
    public string ClaimLink { get; set; } = string.Empty;
    
    /// <summary>
    /// Reward amount in satoshis
    /// </summary>
    public long RewardSatoshis { get; set; }
    
    /// <summary>
    /// Original transaction currency (e.g., USD, EUR)
    /// </summary>
    public string Currency { get; set; } = "USD";
    
    /// <summary>
    /// Reward amount in original currency
    /// </summary>
    public decimal RewardAmount { get; set; }
    
    /// <summary>
    /// Reference transaction ID
    /// </summary>
    public string TransactionId { get; set; } = string.Empty;
    
    /// <summary>
    /// Order ID if available
    /// </summary>
    public string? OrderId { get; set; }
    
    /// <summary>
    /// When the reward was created
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// How long to display the reward (in seconds)
    /// </summary>
    public int DisplayDurationSeconds { get; set; } = 60;
}

