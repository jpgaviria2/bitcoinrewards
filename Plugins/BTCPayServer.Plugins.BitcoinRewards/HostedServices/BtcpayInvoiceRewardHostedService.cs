#nullable enable
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using Microsoft.Extensions.Logging;
using BTCPayServer.Logging;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Plugins.BitcoinRewards.HostedServices;

/// <summary>
/// Listens for BTCPay invoice events and issues rewards for settled invoices that include a buyer email.
/// </summary>
public class BtcpayInvoiceRewardHostedService : EventHostedServiceBase
{
    private readonly StoreRepository _storeRepository;
    private readonly BitcoinRewardsService _rewardsService;
    private readonly ILogger<BtcpayInvoiceRewardHostedService> _logger;

    public BtcpayInvoiceRewardHostedService(
        EventAggregator eventAggregator,
        StoreRepository storeRepository,
        BitcoinRewardsService rewardsService,
        ILogger<BtcpayInvoiceRewardHostedService> logger,
        Logs logs) : base(eventAggregator, logs)
    {
        _storeRepository = storeRepository;
        _rewardsService = rewardsService;
        _logger = logger;
    }

    protected override void SubscribeToEvents()
    {
        Subscribe<InvoiceEvent>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is not InvoiceEvent invoiceEvent)
            return;

        // Only act on settled/paid events
        if (invoiceEvent.EventCode is not (InvoiceEventCode.Completed or InvoiceEventCode.PaidInFull or InvoiceEventCode.PaymentSettled))
            return;

        var invoice = invoiceEvent.Invoice;
        if (invoice is null)
            return;

        var buyerEmail = invoice.Metadata?.BuyerEmail;

        _logger.LogInformation("Processing BTCPay invoice {InvoiceId} for store {StoreId} - BuyerEmail: '{BuyerEmail}'", invoice.Id, invoice.StoreId, buyerEmail);

        // Allow processing even without email - BitcoinRewardsService will handle fallback to display mode
        // This enables rewards to be broadcast to display devices when no email is provided
        // if (string.IsNullOrWhiteSpace(buyerEmail))
        //     return;

        var settings = await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(
            invoice.StoreId,
            BitcoinRewardsStoreSettings.SettingsName);

        if (settings is null || !settings.Enabled)
            return;

        // Platform must include BTCPay flag
        if ((settings.EnabledPlatforms & PlatformFlags.Btcpay) == PlatformFlags.None)
        {
            _logger.LogDebug("BTCPay rewards disabled for store {StoreId}", invoice.StoreId);
            return;
        }

        var transaction = new TransactionData
        {
            TransactionId = invoice.Id,
            OrderId = invoice.Metadata?.OrderId ?? invoice.Id,
            Amount = invoice.Price,
            Currency = invoice.Currency,
            CustomerEmail = buyerEmail,
            CustomerPhone = null,
            Platform = TransactionPlatform.Btcpay,
            TransactionDate = invoice.InvoiceTime.UtcDateTime
        };

        var ok = await _rewardsService.ProcessRewardAsync(invoice.StoreId, transaction);
        if (!ok)
        {
            _logger.LogWarning("Failed to create reward from BTCPay invoice {InvoiceId} for store {StoreId}", invoice.Id, invoice.StoreId);
        }
    }
}

