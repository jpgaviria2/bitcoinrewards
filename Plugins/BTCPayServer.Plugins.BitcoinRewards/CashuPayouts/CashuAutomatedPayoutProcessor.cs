#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.PayoutProcessors;
using BTCPayServer.Payouts;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;
using NBitcoin;
using PayoutData = BTCPayServer.Data.PayoutData;
using PayoutProcessorData = BTCPayServer.Data.PayoutProcessorData;

namespace BTCPayServer.Plugins.BitcoinRewards.CashuPayouts;

/// <summary>
/// Automated payout processor for Cashu tokens in Bitcoin Rewards plugin.
/// NOTE: This is a minimal implementation. For full functionality, ensure BTCNutServer Cashu plugin is installed,
/// which provides the complete payout processor implementation.
/// This processor will check if BTCNutServer's processor is available and log accordingly.
/// </summary>
public class CashuAutomatedPayoutProcessor : BaseAutomatedPayoutProcessor<CashuAutomatedPayoutBlob>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CashuAutomatedPayoutProcessor> _logger;
    
    // Cashu Payment Method ID - must match BTCNutServer's
    private static readonly PaymentMethodId CashuPmid = new PaymentMethodId("CASHU");

    public CashuAutomatedPayoutProcessor(
        PayoutProcessorData payoutProcessorSettings,
        ILoggerFactory loggerFactory,
        StoreRepository storeRepository,
        ApplicationDbContextFactory applicationDbContextFactory,
        PaymentMethodHandlerDictionary paymentHandlers,
        IPluginHookService pluginHookService,
        EventAggregator eventAggregator,
        IServiceProvider serviceProvider) :
        base(
            CashuPmid,
            loggerFactory,
            storeRepository,
            payoutProcessorSettings,
            applicationDbContextFactory,
            paymentHandlers,
            pluginHookService,
            eventAggregator)
    {
        _serviceProvider = serviceProvider;
        _logger = loggerFactory.CreateLogger<CashuAutomatedPayoutProcessor>();
    }

    protected override async Task Process(object paymentMethodConfig, List<PayoutData> payouts)
    {
        // Check if BTCNutServer's payout processor is available
        var cashuAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "btcnutserver-test" || 
                                a.GetName().Name == "BTCPayServer.Plugins.Cashu" ||
                                a.FullName?.Contains("Cashu") == true);
        
        if (cashuAssembly == null)
        {
            Logs.PayServer.LogWarning(
                "BTCNutServer Cashu plugin not found. Payout processor cannot function without it. " +
                "Please install the BTCNutServer plugin to enable Cashu payout processing.");
            return;
        }

        // Check if BTCNutServer's payout processor factory is registered
        var cashuPayoutProcessorFactoryType = cashuAssembly.GetType(
            "BTCPayServer.Plugins.Cashu.Payouts.Cashu.CashuAutomatedPayoutSenderFactory");
        
        if (cashuPayoutProcessorFactoryType != null)
        {
            var existingFactory = _serviceProvider.GetService(cashuPayoutProcessorFactoryType);
            if (existingFactory != null)
            {
                Logs.PayServer.LogInformation(
                    "BTCNutServer's Cashu payout processor is already available. " +
                    "Using BTCNutServer's implementation for store {StoreId}. " +
                    "This Bitcoin Rewards payout processor will skip processing to avoid conflicts.",
                    PayoutProcessorSettings.StoreId);
                return;
            }
        }

        // If we reach here, BTCNutServer plugin is installed but payout processor might not be registered
        // For now, log a warning and skip processing
        // A full implementation would require extensive reflection to access Cashu services
        Logs.PayServer.LogWarning(
            "BTCNutServer Cashu plugin found but payout processor not fully accessible. " +
            "Payout processing for store {StoreId} skipped. " +
            "Please ensure BTCNutServer plugin is properly configured with payout processor enabled.",
            PayoutProcessorSettings.StoreId);
    }
}

