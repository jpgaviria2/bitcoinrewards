#nullable enable
using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

public enum CustomerOrderAssociationState
{
    Pending = 0,
    BoundToPayment = 1,
    Consumed = 2,
    Cancelled = 3
}

/// <summary>
/// Explicitly binds one signed customer profile to one exact Square order. The scanned
/// Lightning address is represented only by a keyed hash; plaintext is re-resolved from the
/// profile service when a future direct payout is dispatched.
/// </summary>
public class CustomerOrderAssociation
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(50)]
    public string StoreId { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string SquareOrderId { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? SquarePaymentId { get; set; }

    [Required, MaxLength(64)]
    public string CustomerProfileId { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string LightningAddressHash { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string LightningAddress { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? RegisterId { get; set; }

    [MaxLength(100)]
    public string? DeviceId { get; set; }

    [MaxLength(100)]
    public string? StaffActorId { get; set; }

    public CustomerOrderAssociationState State { get; set; } = CustomerOrderAssociationState.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [ConcurrencyCheck]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? BoundAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
}
