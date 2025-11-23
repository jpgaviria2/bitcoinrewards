using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services
{
    public class RateService
    {
        private readonly RateProviderFactory _rateProviderFactory;
        private readonly Logs _logs;

        public RateService(
            RateProviderFactory rateProviderFactory,
            Logs logs)
        {
            _rateProviderFactory = rateProviderFactory ?? throw new ArgumentNullException(nameof(rateProviderFactory));
            _logs = logs ?? throw new ArgumentNullException(nameof(logs));
        }

        public async Task<decimal> ConvertToBTC(decimal amount, string currency, string preferredProvider = "coingecko", CancellationToken cancellationToken = default)
        {
            try
            {
                if (currency.ToUpper() == "BTC")
                {
                    return amount; // Already in BTC
                }

                // Normalize provider name (lowercase)
                var providerName = preferredProvider?.ToLowerInvariant() ?? "coingecko";
                
                // Check if provider exists, fallback to coingecko if not
                if (!_rateProviderFactory.Providers.ContainsKey(providerName))
                {
                    _logs.PayServer.LogWarning($"Rate provider {preferredProvider} not found, using coingecko");
                    providerName = "coingecko";
                    
                    if (!_rateProviderFactory.Providers.ContainsKey(providerName))
                    {
                        _logs.PayServer.LogError("No rate provider available, using fallback rate");
                        // Fallback to a reasonable rate (e.g., 50000 USD/BTC)
                        return amount / 50000m;
                    }
                }

                // Create currency pair
                var currencyPair = new CurrencyPair(currency, "BTC");
                
                // Query rates from the provider
                var result = await _rateProviderFactory.QueryRates(providerName, null, cancellationToken);

                if (result?.Exception != null)
                {
                    _logs?.PayServer?.LogWarning(result.Exception.Exception, $"Error querying rate provider {providerName}: {result.Exception.Exception.Message}");
                }
                
                // Find the matching pair rate
                var pairRate = result?.PairRates?.FirstOrDefault(r => r.CurrencyPair == currencyPair);
                
                if (pairRate == null || pairRate.BidAsk == null)
                {
                    _logs?.PayServer?.LogWarning($"Could not get rate for {currency}/BTC from {providerName}, using fallback");
                    return amount / 50000m;
                }

                // Convert amount to BTC using the bid price
                var btcAmount = amount / pairRate.BidAsk.Bid;
                return btcAmount;
            }
            catch (Exception ex)
            {
                _logs?.PayServer?.LogError(ex, $"Failed to convert {amount} {currency} to BTC: {ex.Message}");
                // Fallback to a reasonable rate
                return amount / 50000m;
            }
        }
    }
}

