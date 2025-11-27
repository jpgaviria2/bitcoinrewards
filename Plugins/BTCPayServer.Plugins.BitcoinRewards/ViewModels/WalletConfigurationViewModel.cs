#nullable enable
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.BitcoinRewards.ViewModels;

public class WalletConfigurationViewModel
{
    public string StoreId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mint URL is required")]
    [Url(ErrorMessage = "Must be a valid URL")]
    [Display(Name = "Mint URL")]
    public string MintUrl { get; set; } = string.Empty;
}

