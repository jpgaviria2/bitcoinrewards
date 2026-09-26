#nullable enable
using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

public enum RewardNotificationState
{
    Pending = 0,
    Delivering = 1,
    Delivered = 2,
    Suppressed = 3,
    Failed = 4
}

public sealed class RewardNotificationOutbox
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RewardId { get; set; }
    [MaxLength(100)] public string EventId { get; set; } = string.Empty;
    [MaxLength(64)] public string CustomerProfileId { get; set; } = string.Empty;
    [MaxLength(50)] public string StoreId { get; set; } = string.Empty;
    public long AmountSatoshis { get; set; }
    public DateTime OccurredAt { get; set; }
    public RewardNotificationState State { get; set; } = RewardNotificationState.Pending;
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    [MaxLength(500)] public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
