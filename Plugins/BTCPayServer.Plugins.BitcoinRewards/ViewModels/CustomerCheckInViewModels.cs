#nullable enable
using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.BitcoinRewards.ViewModels;

public class CustomerCheckInViewModel
{
    public string StoreId { get; set; } = string.Empty;

    [Display(Name = "Lightning Address")]
    [Required(ErrorMessage = "Scan or paste your wallet Lightning address.")]
    [MaxLength(128)]
    public string LightningAddress { get; set; } = string.Empty;

    [Display(Name = "Register ID")]
    [MaxLength(100)]
    public string? RegisterId { get; set; }

    [Display(Name = "Device ID")]
    [MaxLength(100)]
    public string? DeviceId { get; set; }

    public bool Saved { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Message { get; set; }
}
