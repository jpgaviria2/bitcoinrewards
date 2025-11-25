#nullable enable
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Plugins.BitcoinRewards.Models;

namespace BTCPayServer.Plugins.BitcoinRewards.ViewModels;

public class CreateTestRewardViewModel
{
    public string StoreId { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Transaction Amount")]
    [Range(0.01, 1000000, ErrorMessage = "Transaction amount must be between 0.01 and 1,000,000")]
    public decimal TransactionAmount { get; set; }

    [Display(Name = "Currency")]
    [MaxLength(10)]
    public string Currency { get; set; } = "USD";

    [Display(Name = "Platform")]
    public TransactionPlatform Platform { get; set; } = TransactionPlatform.Shopify;

    [Display(Name = "Order ID (Optional)")]
    [MaxLength(255)]
    public string? OrderId { get; set; }

    [Required]
    [Display(Name = "Customer Email")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string CustomerEmail { get; set; } = string.Empty;

    [Display(Name = "Customer Phone (Optional)")]
    [Phone(ErrorMessage = "Invalid phone number")]
    public string? CustomerPhone { get; set; }
}

