#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Data;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Converts between BTC (sats) and CAD using BTCPay's built-in rate provider.
/// All CAD values are in cents (long). Exchange rate is expressed as sats per 1 CAD.
/// </summary>
public class ExchangeRateService
{
    private const decimal SatsPerBtc = 100_000_000m;
    private const string DefaultCurrency = "CAD";

    private readonly RateFetcher _rateFetcher;
    private readonly DefaultRulesCollection _defaultRules;
    private readonly StoreRepository _storeRepository;
    private readonly ILogger<ExchangeRateService> _logger;

    public ExchangeRateService(
        RateFetcher rateFetcher,
        DefaultRulesCollection defaultRules,
        StoreRepository storeRepository,
        ILogger<ExchangeRateService> logger)
    {
        _rateFetcher = rateFetcher;
        _defaultRules = defaultRules;
        _storeRepository = storeRepository;
        _logger = logger;
    }

    /// <summary>
    /// Get sats per 1 CAD. E.g. if 1 BTC = $140,000 CAD → ~714 sats/CAD.
    /// </summary>
    public async Task<decimal> GetSatsPerCadAsync(string? storeId = null)
    {
        var cadPerBtc = await GetBtcCadRateAsync(storeId);
        if (cadPerBtc <= 0)
            throw new InvalidOperationException("Could not fetch BTC/CAD exchange rate");

        return SatsPerBtc / cadPerBtc;
    }

    /// <summary>
    /// Convert sats to CAD cents at current rate.
    /// </summary>
    public async Task<(long cadCents, decimal exchangeRate)> SatsToCadCentsAsync(long sats, string? storeId = null)
    {
        var satsPerCad = await GetSatsPerCadAsync(storeId);
        var cadDollars = sats / satsPerCad;
        var cadCents = (long)Math.Round(cadDollars * 100m, MidpointRounding.AwayFromZero);
        return (cadCents, satsPerCad);
    }

    /// <summary>
    /// Convert CAD cents to sats at current rate.
    /// </summary>
    public async Task<(long sats, decimal exchangeRate)> CadCentsToSatsAsync(long cadCents, string? storeId = null)
    {
        var satsPerCad = await GetSatsPerCadAsync(storeId);
        var cadDollars = cadCents / 100m;
        var sats = (long)Math.Round(cadDollars * satsPerCad, MidpointRounding.AwayFromZero);
        return (sats, satsPerCad);
    }

    private async Task<decimal> GetBtcCadRateAsync(string? storeId)
    {
        try
        {
            var pair = new CurrencyPair("BTC", DefaultCurrency);

            var store = !string.IsNullOrEmpty(storeId)
                ? await _storeRepository.FindStore(storeId)
                : null;

            if (store == null)
            {
                _logger.LogWarning("No store found for rate fetch, storeId={StoreId}", storeId);
                return 0m;
            }

            var blob = store.GetStoreBlob();
            var rules = blob.GetRateRules(_defaultRules);

            var result = await _rateFetcher.FetchRate(
                pair,
                rules,
                new StoreIdRateContext(storeId!),
                CancellationToken.None);

            if (result?.BidAsk?.Bid is > 0)
            {
                _logger.LogDebug("BTC/CAD rate: {Rate}", result.BidAsk.Bid);
                return result.BidAsk.Bid;
            }

            _logger.LogWarning("Failed to fetch BTC/CAD rate");
            return 0m;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching BTC/CAD rate");
            return 0m;
        }
    }
}
