#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.PayoutProcessors;
using BTCPayServer.Payouts;
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
    private readonly StoreRepository _storeRepository;
    private readonly PayoutProcessorService _payoutProcessorService;
    private readonly ILogger<PayoutProcessorDiscoveryService> _logger;

    public PayoutProcessorDiscoveryService(
        IEnumerable<IPayoutProcessorFactory> payoutProcessorFactories,
        StoreRepository storeRepository,
        PayoutProcessorService payoutProcessorService,
        ILogger<PayoutProcessorDiscoveryService> logger)
    {
        _payoutProcessorFactories = payoutProcessorFactories;
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
            
            options.Add(new PayoutProcessorOption
            {
                FactoryName = factory.Processor,
                FriendlyName = factory.FriendlyName,
                SupportedMethods = new List<PayoutMethodId> { payoutMethodId },
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
                
                _logger.LogDebug("Found payout processor factory: {ProcessorName} ({FriendlyName}), SupportedMethods: {Methods}",
                    factory.Processor, factory.FriendlyName, string.Join(", ", supportedMethods.Select(m => m.ToString())));

                var option = new PayoutProcessorOption
                {
                    FactoryName = factory.Processor,
                    FriendlyName = factory.FriendlyName,
                    SupportedMethods = supportedMethods,
                    IsAvailable = true
                };

                options.Add(option);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing payout processor factory {FactoryName}", factory.Processor);
            }
        }

        _logger.LogDebug("Total payout processors discovered for store {StoreId}: {Count}", storeId, options.Count);
        return options;
    }
}

