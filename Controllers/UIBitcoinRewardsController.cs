using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers
{
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class UIBitcoinRewardsController : Controller
    {
        private readonly StoreRepository _storeRepository;
        private readonly BitcoinRewardsService _rewardsService;

        public UIBitcoinRewardsController(
            StoreRepository storeRepository,
            BitcoinRewardsService rewardsService)
        {
            _storeRepository = storeRepository;
            _rewardsService = rewardsService;
        }

        public StoreData CurrentStore
        {
            get
            {
                return this.HttpContext.GetStoreData();
            }
        }

        [HttpGet]
        [Route("stores/{storeId}/plugins/bitcoinrewards")]
        public IActionResult EditSettings()
        {
            var blob = CurrentStore.GetStoreBlob();
            var settings = BitcoinRewards.BitcoinRewardsExtensions.GetBitcoinRewardsSettings(blob) ?? new BitcoinRewardsSettings();
            return View(settings);
        }

        [HttpPost("stores/{storeId}/plugins/bitcoinrewards")]
        public async Task<IActionResult> EditSettings(string storeId, BitcoinRewardsSettings vm, string command = "")
        {
            switch (command)
            {
                case "Save":
                    {
                        var blob = CurrentStore.GetStoreBlob();
                        if (vm.Enabled && !vm.CredentialsPopulated())
                        {
                            TempData[WellKnownTempData.ErrorMessage] = "Please provide a webhook secret when enabling rewards";
                            return View(vm);
                        }

                        if (vm.Enabled && vm.IntegratedAt == null)
                        {
                            vm.IntegratedAt = System.DateTimeOffset.Now.DateTime;
                        }

                        blob.SetBitcoinRewardsSettings(vm);
                        if (CurrentStore.SetStoreBlob(blob))
                        {
                            await _storeRepository.UpdateStore(CurrentStore);
                        }

                        TempData[WellKnownTempData.SuccessMessage] = "Bitcoin Rewards settings updated successfully";
                        break;
                    }
                case "Disable":
                    {
                        var blob = CurrentStore.GetStoreBlob();
                        var settings = BitcoinRewards.BitcoinRewardsExtensions.GetBitcoinRewardsSettings(blob);
                        if (settings != null)
                        {
                            settings.Enabled = false;
                            blob.SetBitcoinRewardsSettings(settings);
                            if (CurrentStore.SetStoreBlob(blob))
                            {
                                await _storeRepository.UpdateStore(CurrentStore);
                            }
                        }

                        TempData[WellKnownTempData.SuccessMessage] = "Bitcoin Rewards disabled";
                        break;
                    }
            }

            return RedirectToAction(nameof(EditSettings), new { storeId = CurrentStore.Id });
        }

        [HttpGet]
        [Route("stores/{storeId}/plugins/bitcoinrewards/rewards")]
        public async Task<IActionResult> ViewRewards()
        {
            var rewards = await _rewardsService.GetRewardsForStore(CurrentStore.Id);
            return View(rewards);
        }
    }
}

