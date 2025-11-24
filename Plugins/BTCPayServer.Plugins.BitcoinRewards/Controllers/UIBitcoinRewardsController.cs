using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewProfile)]
public class UIBitcoinRewardsController : Controller
{
    [HttpGet]
    [Route("plugins/bitcoin-rewards/{storeId}/settings")]
    public IActionResult EditSettings(string storeId)
    {
        ViewData.SetActivePage("BitcoinRewards", "Bitcoin Rewards Settings", "BitcoinRewards");
        return View("EditSettings");
    }
}

