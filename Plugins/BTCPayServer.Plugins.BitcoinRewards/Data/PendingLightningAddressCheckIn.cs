#nullable enable
using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

public enum PendingLightningAddressCheckInState
{
    Pending = 0,
    Consumed = 1,
    Expired = 2,
    Cancelled = 3
}

/// <summary>
/// Short-lived Lightning address captured from a customer wallet QR. The next
/// Square completed payment for the store consumes one pending check-in.
/// </summary>
public class PendingLightningAddressCheckIn
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(50)]
    public string StoreId { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string LightningAddress { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string LightningAddressHash { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? RegisterId { get; set; }

    [MaxLength(100)]
    public string? DeviceId { get; set; }

    [MaxLength(50)]
    public string Source { get; set; } = "customer-wallet-qr";

    [MaxLength(255)]
    public string? SquareOrderId { get; set; }

    [MaxLength(255)]
    public string? SquarePaymentId { get; set; }

    public PendingLightningAddressCheckInState State { get; set; } = PendingLightningAddressCheckInState.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [ConcurrencyCheck]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(5);
    public DateTime? ConsumedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
}
