#nullable enable
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.BitcoinRewards.ViewModels;

public class TopUpViewModel
{
    public string StoreId { get; set; } = string.Empty;
    public string MintUrl { get; set; } = string.Empty;
    public ulong CurrentBalance { get; set; }

    [Display(Name = "Cashu Token")]
    public string? Token { get; set; }
}

