#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

public enum RewardStatus
{
    Pending = 0,
    Sent = 1,
    Redeemed = 2,
    Expired = 3,
    Reclaimed = 4
}

public enum RewardPlatform
{
    Shopify = 0,
    Square = 1
}

public class BitcoinRewardRecord
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    [MaxLength(50)]
    public string StoreId { get; set; } = string.Empty;
    
    [Required]
    public RewardPlatform Platform { get; set; }
    
    [Required]
    [MaxLength(255)]
    public string TransactionId { get; set; } = string.Empty;
    
    [MaxLength(255)]
    public string? OrderId { get; set; }
    
    [MaxLength(255)]
    public string? CustomerEmail { get; set; }
    
    [MaxLength(50)]
    public string? CustomerPhone { get; set; }
    
    [Required]
    [Column(TypeName = "decimal(18,8)")]
    public decimal TransactionAmount { get; set; }
    
    [Required]
    [MaxLength(10)]
    public string Currency { get; set; } = "USD";
    
    [Required]
    [Column(TypeName = "decimal(18,8)")]
    public decimal RewardAmount { get; set; }
    
    [Required]
    public long RewardAmountSatoshis { get; set; }
    
    [MaxLength(100)]
    public string? PullPaymentId { get; set; }

    [MaxLength(100)]
    public string? PayoutId { get; set; }

    [MaxLength(255)]
    public string? PayoutProcessor { get; set; }

    [MaxLength(100)]
    public string? PayoutMethod { get; set; }

    [MaxLength(2000)]
    public string? ClaimLink { get; set; }
    
    [Required]
    public RewardStatus Status { get; set; } = RewardStatus.Pending;
    
    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? SentAt { get; set; }
    
    public DateTime? RedeemedAt { get; set; }
    
    public DateTime? ExpiresAt { get; set; }

    public DateTime? ClaimedAt { get; set; }

    public DateTime? PaidAt { get; set; }
    
    [MaxLength(500)]
    public string? ErrorMessage { get; set; }
    
    public int RetryCount { get; set; } = 0;
}

