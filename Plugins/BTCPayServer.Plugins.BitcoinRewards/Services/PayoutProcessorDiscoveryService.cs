#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.BitcoinRewards.CashuPayouts;
using BTCPayServer.Plugins.BitcoinRewards.ViewModels;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Service for discovering available payout processors and checking Cashu wallet availability
/// </summary>
public class PayoutProcessorDiscoveryService
{
    private readonly IEnumerable<IPayoutProcessorFactory> _payoutProcessorFactories;
    private readonly PaymentMethodHandlerDictionary _paymentHandlers;
    private readonly StoreRepository _storeRepository;
    private readonly ILogger<PayoutProcessorDiscoveryService> _logger;
    private static readonly PaymentMethodId CashuPmid = new PaymentMethodId("CASHU");

    public PayoutProcessorDiscoveryService(
        IEnumerable<IPayoutProcessorFactory> payoutProcessorFactories,
        PaymentMethodHandlerDictionary paymentHandlers,
        StoreRepository storeRepository,
        ILogger<PayoutProcessorDiscoveryService> logger)
    {
        _payoutProcessorFactories = payoutProcessorFactories;
        _paymentHandlers = paymentHandlers;
        _storeRepository = storeRepository;
        _logger = logger;
    }

    /// <summary>
    /// Get all available payout processors for the given store
    /// </summary>
    public async Task<List<PayoutProcessorOption>> GetAvailablePayoutProcessorsAsync(string storeId)
    {
        var options = new List<PayoutProcessorOption>();
        var store = await _storeRepository.FindStore(storeId);
        
        if (store == null)
        {
            _logger.LogWarning("Store {StoreId} not found", storeId);
            return options;
        }

        foreach (var factory in _payoutProcessorFactories)
        {
            try
            {
                var supportedMethods = factory.GetSupportedPayoutMethods().ToList();
                var isCashu = IsCashuProcessor(factory);
                
                var option = new PayoutProcessorOption
                {
                    FactoryName = factory.Processor,
                    FriendlyName = factory.FriendlyName,
                    SupportedMethods = supportedMethods,
                    IsCashu = isCashu,
                    IsAvailable = true
                };

                // Check if Cashu processor is available (wallet installed)
                if (isCashu)
                {
                    var cashuAvailable = await IsCashuWalletInstalledAsync(storeId);
                    option.IsAvailable = cashuAvailable;
                    if (!cashuAvailable)
                    {
                        option.UnavailableReason = "Cashu wallet not installed or not configured";
                    }
                }

                options.Add(option);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing payout processor factory {FactoryName}", factory.Processor);
            }
        }

        return options;
    }

    /// <summary>
    /// Check if a payout processor factory is the Cashu processor
    /// </summary>
    private bool IsCashuProcessor(IPayoutProcessorFactory factory)
    {
        // Check if it's our Cashu processor or BTCNutServer's Cashu processor
        var processorName = factory.Processor;
        return processorName.Contains("Cashu", StringComparison.OrdinalIgnoreCase) ||
               processorName == "BitcoinRewardsCashuAutomatedPayoutSender" ||
               processorName == "CashuAutomatedPayoutSenderFactory";
    }

    /// <summary>
    /// Check if Cashu wallet is installed and configured for the store
    /// </summary>
    public async Task<bool> IsCashuWalletInstalledAsync(string storeId)
    {
        try
        {
            var store = await _storeRepository.FindStore(storeId);
            if (store == null)
                return false;

            // Try to use CashuPayoutHandler to check if Cashu is supported
            var cashuAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "btcnutserver-test" || 
                                    a.GetName().Name == "BTCPayServer.Plugins.Cashu" ||
                                    a.FullName?.Contains("Cashu") == true);
            
            if (cashuAssembly == null)
            {
                _logger.LogDebug("Cashu plugin assembly not found for store {StoreId}", storeId);
                return false;
            }

            // Check if Cashu payment method is configured
            var config = store.GetPaymentMethodConfig(CashuPmid, _paymentHandlers);
            if (config == null)
            {
                _logger.LogDebug("Cashu payment method config not found for store {StoreId}", storeId);
                return false;
            }

            // Check if TrustedMintsUrls is configured
            var trustedMintsProperty = config.GetType().GetProperty("TrustedMintsUrls");
            if (trustedMintsProperty == null)
            {
                _logger.LogDebug("TrustedMintsUrls property not found in Cashu config for store {StoreId}", storeId);
                return false;
            }

            var trustedMints = trustedMintsProperty.GetValue(config) as List<string>;
            var hasTrustedMints = trustedMints != null && trustedMints.Count > 0;
            
            _logger.LogDebug("Cashu wallet check for store {StoreId}: {HasWallet}", storeId, hasTrustedMints);
            return hasTrustedMints;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if Cashu wallet is installed for store {StoreId}", storeId);
            return false;
        }
    }
}

