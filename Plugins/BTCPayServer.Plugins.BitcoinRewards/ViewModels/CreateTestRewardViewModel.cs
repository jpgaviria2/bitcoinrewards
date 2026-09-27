#nullable enable
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Data;

namespace BTCPayServer.Plugins.BitcoinRewards.ViewModels;

public class CreateTestRewardViewModel
{
    public string StoreId { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Test type")]
    public string TestMode { get; set; } = TestModes.ScannedAddress;

    [Display(Name = "Test reward amount (sats)")]
    [Range(1, 100_000, ErrorMessage = "Test reward amount must be between 1 and 100,000 sats")]
    public long TestRewardSatoshis { get; set; } = 100;

    [Display(Name = "Customer email")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string? CustomerEmail { get; set; }

    [Display(Name = "Currency")]
    [MaxLength(10)]
    public string Currency { get; set; } = StoreBlob.StandardDefaultCurrency;

    [Display(Name = "Order ID (Optional)")]
    [MaxLength(255)]
    public string? OrderId { get; set; }

    [Display(Name = "Lightning Address")]
    [MaxLength(128)]
    public string? LightningAddress { get; set; }

    // Legacy/advanced fields retained for model compatibility.
    [Display(Name = "Transaction Amount")]
    [Range(0.01, 1000000, ErrorMessage = "Transaction amount must be between 0.01 and 1,000,000")]
    public decimal TransactionAmount { get; set; } = 0.01m;

    [Display(Name = "Platform")]
    public TransactionPlatform Platform { get; set; } = TransactionPlatform.Square;

    [Display(Name = "Customer Phone (Optional)")]
    [Phone(ErrorMessage = "Invalid phone number")]
    public string? CustomerPhone { get; set; }

    public static class TestModes
    {
        public const string ScannedAddress = "scanned-address";
        public const string Email = "email";
        public const string Display = "display";
    }
}
