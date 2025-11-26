using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.PayoutProcessors;
using BTCPayServer.Payouts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.BitcoinRewards.CashuPayouts;

public class UICashuAutomatedPayoutProcessorsController : Controller
{
    private readonly EventAggregator _eventAggregator;
    private readonly CashuAutomatedPayoutSenderFactory _cashuAutomatedPayoutSenderFactory;
    private readonly PayoutProcessorService _payoutProcessorService;
    private IStringLocalizer StringLocalizer { get; }

    public UICashuAutomatedPayoutProcessorsController(
        EventAggregator eventAggregator,
        CashuAutomatedPayoutSenderFactory cashuAutomatedPayoutSenderFactory,
        PayoutProcessorService payoutProcessorService,
        IStringLocalizer stringLocalizer)
    {
        _eventAggregator = eventAggregator;
        _cashuAutomatedPayoutSenderFactory = cashuAutomatedPayoutSenderFactory;
        _payoutProcessorService = payoutProcessorService;
        StringLocalizer = stringLocalizer;
    }

    private static PayoutMethodId GetPayoutMethodId() => PayoutMethodId.Parse("CASHU");

    [HttpGet("~/stores/{storeId}/payout-processors/cashu-automated")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Configure(string storeId)
    {
        var id = GetPayoutMethodId();
        if (!_cashuAutomatedPayoutSenderFactory.GetSupportedPayoutMethods().Any(i => id == i))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = StringLocalizer["This processor cannot handle Cashu payouts."].Value
            });
            return RedirectToAction("ConfigureStorePayoutProcessors", "UiPayoutProcessors");
        }

        var activeProcessor =
            (await _payoutProcessorService.GetProcessors(
                new PayoutProcessorService.PayoutProcessorQuery()
                {
                    Stores = new[] { storeId },
                    Processors = new[] { _cashuAutomatedPayoutSenderFactory.Processor },
                    PayoutMethods = new[] { GetPayoutMethodId() }
                }))
            .FirstOrDefault();

        ViewData["StoreId"] = storeId;
        CashuAutomatedPayoutBlob blob;
        if (activeProcessor is null)
        {
            blob = new CashuAutomatedPayoutBlob
            {
                Interval = TimeSpan.FromMinutes(AutomatedPayoutConstants.DefaultIntervalMinutes),
                ProcessNewPayoutsInstantly = false
            };
        }
        else
        {
            blob = CashuAutomatedPayoutProcessor.GetBlob(activeProcessor);
            // Ensure interval has a valid value
            if (blob.Interval == TimeSpan.Zero)
            {
                blob.Interval = TimeSpan.FromMinutes(AutomatedPayoutConstants.DefaultIntervalMinutes);
            }
        }
        return View(new CashuTransferViewModel(blob));
    }

    [HttpPost("~/stores/{storeId}/payout-processors/cashu-automated")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Configure(string storeId, CashuTransferViewModel automatedTransferBlob)
    {
        if (!ModelState.IsValid)
            return View(automatedTransferBlob);

        var id = GetPayoutMethodId();
        if (!_cashuAutomatedPayoutSenderFactory.GetSupportedPayoutMethods().Any(i => id == i))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = StringLocalizer["This processor cannot handle Cashu payouts."].Value
            });
            return RedirectToAction("ConfigureStorePayoutProcessors", "UiPayoutProcessors");
        }

        var activeProcessor =
            (await _payoutProcessorService.GetProcessors(
                new PayoutProcessorService.PayoutProcessorQuery()
                {
                    Stores = new[] { storeId },
                    Processors = new[] { _cashuAutomatedPayoutSenderFactory.Processor },
                    PayoutMethods = new[] { GetPayoutMethodId() }
                }))
            .FirstOrDefault();

        activeProcessor ??= new PayoutProcessorData();
        activeProcessor.HasTypedBlob<CashuAutomatedPayoutBlob>().SetBlob(automatedTransferBlob.ToBlob());
        activeProcessor.StoreId = storeId;
        activeProcessor.PayoutMethodId = GetPayoutMethodId().ToString();
        activeProcessor.Processor = _cashuAutomatedPayoutSenderFactory.Processor;
        
        var tcs = new TaskCompletionSource();
        _eventAggregator.Publish(new PayoutProcessorUpdated()
        {
            Data = activeProcessor,
            Id = activeProcessor.Id,
            Processed = tcs
        });
        
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = StatusMessageModel.StatusSeverity.Success,
            Message = StringLocalizer["Processor updated."].Value
        });
        
        await tcs.Task;
        return RedirectToAction("ConfigureStorePayoutProcessors", "UiPayoutProcessors", new { storeId });
    }

    public class CashuTransferViewModel
    {
        public CashuTransferViewModel()
        {
        }

        public CashuTransferViewModel(CashuAutomatedPayoutBlob blob)
        {
            if (blob == null)
            {
                IntervalMinutes = AutomatedPayoutConstants.DefaultIntervalMinutes;
                ProcessNewPayoutsInstantly = false;
            }
            else
            {
                // Ensure interval has a valid value
                if (blob.Interval == TimeSpan.Zero || blob.Interval.TotalMinutes < AutomatedPayoutConstants.MinIntervalMinutes)
                {
                    IntervalMinutes = AutomatedPayoutConstants.DefaultIntervalMinutes;
                }
                else
                {
                    IntervalMinutes = blob.Interval.TotalMinutes;
                }
                ProcessNewPayoutsInstantly = blob.ProcessNewPayoutsInstantly;
            }
        }

        [Display(Name = "Process approved payouts instantly")]
        public bool ProcessNewPayoutsInstantly { get; set; }

        [Range(AutomatedPayoutConstants.MinIntervalMinutes, AutomatedPayoutConstants.MaxIntervalMinutes)]
        public double IntervalMinutes { get; set; }

        public CashuAutomatedPayoutBlob ToBlob()
        {
            return new CashuAutomatedPayoutBlob
            {
                ProcessNewPayoutsInstantly = ProcessNewPayoutsInstantly,
                Interval = TimeSpan.FromMinutes(IntervalMinutes),
            };
        }
    }
}

