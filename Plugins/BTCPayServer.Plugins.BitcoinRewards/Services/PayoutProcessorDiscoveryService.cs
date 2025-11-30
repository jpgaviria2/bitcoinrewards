#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.PayoutProcessors;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.BitcoinRewards.CashuPayouts;
using BTCPayServer.Plugins.BitcoinRewards.ViewModels;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;
using PayoutProcessorData = BTCPayServer.Data.PayoutProcessorData;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Service for discovering available payout processors and checking Cashu wallet availability
/// </summary>
public class PayoutProcessorDiscoveryService
{
    private readonly IEnumerable<IPayoutProcessorFactory> _payoutProcessorFactories;
    private readonly BTCPayServer.Services.Invoices.PaymentMethodHandlerDictionary _paymentHandlers;
    private readonly StoreRepository _storeRepository;
    private readonly PayoutProcessorService _payoutProcessorService;
    private readonly ILogger<PayoutProcessorDiscoveryService> _logger;
    private static readonly PaymentMethodId CashuPmid = new PaymentMethodId("CASHU");
    private static readonly PayoutMethodId CashuPayoutPmid = PayoutMethodId.Parse("CASHU");

    public PayoutProcessorDiscoveryService(
        IEnumerable<IPayoutProcessorFactory> payoutProcessorFactories,
        BTCPayServer.Services.Invoices.PaymentMethodHandlerDictionary paymentHandlers,
        StoreRepository storeRepository,
        PayoutProcessorService payoutProcessorService,
        ILogger<PayoutProcessorDiscoveryService> logger)
    {
        _payoutProcessorFactories = payoutProcessorFactories;
        _paymentHandlers = paymentHandlers;
        _storeRepository = storeRepository;
        _payoutProcessorService = payoutProcessorService;
        _logger = logger;
    }

    /// <summary>
    /// Get all configured payout processors for the given store (for use in dropdown selection)
    /// </summary>
    public async Task<List<PayoutProcessorOption>> GetConfiguredPayoutProcessorsAsync(string storeId)
    {
        var options = new List<PayoutProcessorOption>();
        
        // Get all configured processors for this store
        var configuredProcessors = await _payoutProcessorService.GetProcessors(
            new PayoutProcessorService.PayoutProcessorQuery() { Stores = new[] { storeId } });
        
        foreach (var configuredProcessor in configuredProcessors)
        {
            var factory = _payoutProcessorFactories.FirstOrDefault(f => f.Processor == configuredProcessor.Processor);
            if (factory == null)
                continue;
                
            var payoutMethodId = configuredProcessor.GetPayoutMethodId();
            var isCashu = IsCashuProcessor(factory);
            
            options.Add(new PayoutProcessorOption
            {
                FactoryName = factory.Processor,
                FriendlyName = factory.FriendlyName,
                SupportedMethods = new List<PayoutMethodId> { payoutMethodId },
                IsCashu = isCashu,
                IsAvailable = true,
                IsConfigured = true,
                ProcessorId = $"{configuredProcessor.Processor}:{payoutMethodId}"
            });
        }
        
        return options;
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
                
                _logger.LogDebug("Found payout processor factory: {ProcessorName} ({FriendlyName}), IsCashu: {IsCashu}, SupportedMethods: {Methods}",
                    factory.Processor, factory.FriendlyName, isCashu, string.Join(", ", supportedMethods.Select(m => m.ToString())));

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
                    _logger.LogDebug("Cashu processor availability for store {StoreId}: {Available}", storeId, cashuAvailable);
                }

                options.Add(option);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing payout processor factory {FactoryName}", factory.Processor);
            }
        }

        // If Cashu wallet is installed but no Cashu processor was found, add our Bitcoin Rewards Cashu processor
        var cashuWalletAvailable = await IsCashuWalletInstalledAsync(storeId);
        var cashuProcessorExists = options.Any(o => o.IsCashu);
        
        _logger.LogDebug("Payout processor discovery for store {StoreId}: Total factories found={Total}, Cashu wallet available={CashuAvailable}, Cashu processor in list={CashuInList}",
            storeId, _payoutProcessorFactories.Count(), cashuWalletAvailable, cashuProcessorExists);
        
        if (cashuWalletAvailable && !cashuProcessorExists)
        {
            _logger.LogInformation("Cashu wallet is available but no Cashu processor found in factory list. Adding Bitcoin Rewards Cashu processor option for store {StoreId}.", storeId);
            
            // Try to find any Cashu factory that might exist but wasn't detected
            var allFactories = _payoutProcessorFactories.ToList();
            var cashuFactory = allFactories.FirstOrDefault(f => 
                f.Processor.Contains("Cashu", StringComparison.OrdinalIgnoreCase));
            
            if (cashuFactory != null)
            {
                _logger.LogDebug("Found Cashu factory '{FactoryName}' that wasn't detected earlier. Adding it now.", cashuFactory.Processor);
                var supportedMethods = cashuFactory.GetSupportedPayoutMethods().ToList();
                options.Add(new PayoutProcessorOption
                {
                    FactoryName = cashuFactory.Processor,
                    FriendlyName = cashuFactory.FriendlyName,
                    SupportedMethods = supportedMethods,
                    IsCashu = true,
                    IsAvailable = true
                });
            }
            else
            {
                // Add our Bitcoin Rewards Cashu processor as a manual option
                // This ensures users can select it even if the factory registration had issues
                _logger.LogInformation("No Cashu factory found in DI. Adding manual Cashu processor option for store {StoreId}.", storeId);
                options.Add(new PayoutProcessorOption
                {
                    FactoryName = "BitcoinRewardsCashuAutomatedPayoutSender",
                    FriendlyName = "Cashu Automated Payout Sender (Bitcoin Rewards)",
                    SupportedMethods = new List<PayoutMethodId> { CashuPayoutPmid },
                    IsCashu = true,
                    IsAvailable = true
                });
            }
        }

        _logger.LogDebug("Total payout processors discovered for store {StoreId}: {Count}", storeId, options.Count);
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
    /// Returns true if:
    /// 1. BTCNutServer Cashu plugin is installed (assembly found)
    /// 2. Cashu payment method is configured for the store
    /// 3. TrustedMintsUrls is configured (indicates wallet is active)
    /// </summary>
    public async Task<bool> IsCashuWalletInstalledAsync(string storeId)
    {
        try
        {
            var store = await _storeRepository.FindStore(storeId);
            if (store == null)
            {
                _logger.LogDebug("Store {StoreId} not found", storeId);
                return false;
            }

            // Step 1: Check if BTCNutServer Cashu plugin assembly is loaded
            var cashuAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "btcnutserver-test" || 
                                    a.GetName().Name == "BTCPayServer.Plugins.Cashu" ||
                                    a.FullName?.Contains("Cashu") == true ||
                                    a.GetTypes().Any(t => t.Namespace?.Contains("Cashu") == true));
            
            if (cashuAssembly == null)
            {
                _logger.LogDebug("BTCNutServer Cashu plugin assembly not found for store {StoreId}", storeId);
                return false;
            }

            _logger.LogDebug("BTCNutServer Cashu plugin assembly found: {AssemblyName} for store {StoreId}", 
                cashuAssembly.GetName().Name, storeId);

            // Step 2: Check if Cashu payment method handler is registered
            if (!_paymentHandlers.TryGetValue(CashuPmid, out var handler))
            {
                _logger.LogDebug("Cashu payment method handler not registered for store {StoreId}", storeId);
                return false;
            }

            _logger.LogDebug("Cashu payment method handler registered: {HandlerType} for store {StoreId}", 
                handler.GetType().Name, storeId);

            // Step 3: Check if Cashu payment method is configured for this store
            var config = store.GetPaymentMethodConfig(CashuPmid, _paymentHandlers);
            if (config == null)
            {
                _logger.LogDebug("Cashu payment method config not found for store {StoreId}", storeId);
                return false;
            }

            _logger.LogDebug("Cashu payment method config found for store {StoreId}", storeId);

            // Step 4: Check if TrustedMintsUrls is configured (indicates wallet is active)
            var trustedMintsProperty = config.GetType().GetProperty("TrustedMintsUrls");
            if (trustedMintsProperty == null)
            {
                _logger.LogDebug("TrustedMintsUrls property not found in Cashu config for store {StoreId}. Config type: {ConfigType}", 
                    storeId, config.GetType().FullName);
                return false;
            }

            var trustedMints = trustedMintsProperty.GetValue(config) as List<string>;
            var hasTrustedMints = trustedMints != null && trustedMints.Count > 0;
            
            if (hasTrustedMints)
            {
                _logger.LogInformation("Cashu wallet is installed and active for store {StoreId}. Trusted mints count: {Count}", 
                    storeId, trustedMints?.Count ?? 0);
            }
            else
            {
                _logger.LogDebug("Cashu wallet plugin installed but not configured (no trusted mints) for store {StoreId}", storeId);
            }
            
            return hasTrustedMints;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if Cashu wallet is installed for store {StoreId}", storeId);
            return false;
        }
    }

    /// <summary>
    /// Get detailed information about Cashu wallet status
    /// </summary>
    public async Task<(bool PluginInstalled, bool WalletConfigured, string? Details)> GetCashuWalletStatusAsync(string storeId)
    {
        try
        {
            var store = await _storeRepository.FindStore(storeId);
            if (store == null)
                return (false, false, "Store not found");

            // Check if plugin assembly exists
            var cashuAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "btcnutserver-test" || 
                                    a.GetName().Name == "BTCPayServer.Plugins.Cashu" ||
                                    a.FullName?.Contains("Cashu") == true ||
                                    a.GetTypes().Any(t => t.Namespace?.Contains("Cashu") == true));
            
            if (cashuAssembly == null)
                return (false, false, "BTCNutServer Cashu plugin not installed");

            // Check if payment handler is registered
            if (!_paymentHandlers.TryGetValue(CashuPmid, out var handler))
                return (true, false, "Plugin installed but payment handler not registered");

            // Check if configured
            var config = store.GetPaymentMethodConfig(CashuPmid, _paymentHandlers);
            if (config == null)
                return (true, false, "Plugin installed but not configured for this store");

            // Check if wallet is active
            var trustedMintsProperty = config.GetType().GetProperty("TrustedMintsUrls");
            if (trustedMintsProperty == null)
                return (true, false, "Plugin installed but TrustedMintsUrls property not found");

            var trustedMints = trustedMintsProperty.GetValue(config) as List<string>;
            var hasTrustedMints = trustedMints != null && trustedMints.Count > 0;

            if (hasTrustedMints && trustedMints != null)
                return (true, true, $"Plugin installed and wallet active with {trustedMints.Count} trusted mint(s)");
            else
                return (true, false, "Plugin installed but no trusted mints configured");

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Cashu wallet status for store {StoreId}", storeId);
            return (false, false, $"Error: {ex.Message}");
        }
    }
}

