#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;
using BTCPayServer.Data;
using System.Linq;
using BTCPayServer.Services.Rates;
using BTCPayServer.Rating;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

public class BitcoinRewardsService
{
    private readonly StoreRepository _storeRepository;
    private readonly BitcoinRewardsRepository _repository;
    private readonly IEmailNotificationService _emailService;
    private readonly RewardPullPaymentService _pullPaymentService;
    private readonly ILogger<BitcoinRewardsService> _logger;
    private readonly RateFetcher _rateFetcher;

    public BitcoinRewardsService(
        StoreRepository storeRepository,
        BitcoinRewardsRepository repository,
        IEmailNotificationService emailService,
        ILogger<BitcoinRewardsService> logger,
        RewardPullPaymentService pullPaymentService,
        RateFetcher rateFetcher)
    {
        _storeRepository = storeRepository;
        _repository = repository;
        _emailService = emailService;
        _pullPaymentService = pullPaymentService;
        _logger = logger;
        _rateFetcher = rateFetcher;
    }

    public async Task<bool> ProcessRewardAsync(string storeId, TransactionData transaction)
    {
        try
        {
            // Get store settings
            var settings = await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(
                storeId, 
                BitcoinRewardsStoreSettings.SettingsName);
            var store = await _storeRepository.FindStore(storeId);
            var storeCurrency = store?.GetStoreBlob().DefaultCurrency ?? StoreBlob.StandardDefaultCurrency;

            if (settings == null || !settings.Enabled)
            {
                _logger.LogWarning("Bitcoin Rewards not enabled or settings missing for store {StoreId}", storeId);
                return false;
            }

            var isTestReward = transaction.TransactionId.StartsWith("TEST_", StringComparison.OrdinalIgnoreCase);

            // Check if platform is enabled (skip for test rewards)
            // For manual test rewards, skip platform validation if no platform flags are set
            if (!isTestReward && settings.EnabledPlatforms != PlatformFlags.None)
            {
                var platform = transaction.Platform switch
                {
                    TransactionPlatform.Shopify => PlatformFlags.Shopify,
                    TransactionPlatform.Square => PlatformFlags.Square,
                    TransactionPlatform.Btcpay => PlatformFlags.Btcpay,
                    _ => PlatformFlags.None
                };
                
                if ((settings.EnabledPlatforms & platform) == PlatformFlags.None)
                {
                    _logger.LogWarning("Platform {Platform} not enabled for store {StoreId}. Enabled platforms: {EnabledPlatforms}", 
                        transaction.Platform, storeId, settings.EnabledPlatforms);
                    return false;
                }
            }
            else
            {
                _logger.LogInformation("Platform validation skipped for store {StoreId} - no platforms explicitly enabled (likely manual test)", storeId);
            }

            // Check minimum transaction amount
            // Convert transaction amount to store currency for min check
            var txAmountInStoreCurrency = await ConvertToStoreCurrencyAsync(transaction.Amount, transaction.Currency, storeCurrency);

            if (settings.MinimumTransactionAmount.HasValue && 
                txAmountInStoreCurrency < settings.MinimumTransactionAmount.Value)
            {
                _logger.LogWarning("Transaction amount {Amount} {TxCurrency} (~{StoreAmount} {StoreCurrency}) below minimum {Minimum} for store {StoreId}", 
                    transaction.Amount, transaction.Currency, txAmountInStoreCurrency, storeCurrency, settings.MinimumTransactionAmount, storeId);
                return false;
            }

            // Check if transaction already processed
            var platformEnum = transaction.Platform switch
            {
                TransactionPlatform.Shopify => RewardPlatform.Shopify,
                TransactionPlatform.Square => RewardPlatform.Square,
                TransactionPlatform.Btcpay => RewardPlatform.Btcpay,
                _ => RewardPlatform.Shopify
            };
            
            if (await _repository.TransactionExistsAsync(storeId, transaction.TransactionId, platformEnum))
            {
                _logger.LogWarning("Transaction {TransactionId} already processed for store {StoreId}", 
                    transaction.TransactionId, storeId);
                return false;
            }

            // Calculate reward amount
            decimal rewardAmount;
            if (isTestReward)
            {
                // Test rewards always pay 100% of the entered amount (ignore store percentage)
                rewardAmount = transaction.Amount > 0 ? transaction.Amount : 1m;
                _logger.LogInformation("Test reward using full transaction amount {Amount} {Currency}", transaction.Amount, transaction.Currency);
            }
            else
            {
                var percentage = transaction.Platform == TransactionPlatform.Btcpay
                    ? settings.BtcpayRewardPercentage
                    : settings.ExternalRewardPercentage > 0 ? settings.ExternalRewardPercentage : settings.RewardPercentage;
                rewardAmount = CalculateRewardAmount(transaction.Amount, percentage);
                _logger.LogInformation("Reward calculation for {Platform} transaction {TransactionId}: Amount={Amount} {Currency}, Percentage={Percentage}%, RewardAmount={RewardAmount}",
                    transaction.Platform, transaction.TransactionId, transaction.Amount, transaction.Currency, percentage, rewardAmount);
            }

            if (rewardAmount <= 0)
            {
                _logger.LogWarning("Calculated reward amount is zero or negative ({RewardAmount}) for transaction {TransactionId} in store {StoreId}; aborting reward creation",
                    rewardAmount, transaction.TransactionId, storeId);
                return false;
            }
            
            // Convert reward amount from store currency to BTC/satoshis
            var rewardSatoshis = await ConvertToSatoshisAsync(transaction.Currency, rewardAmount, storeId);
            if (rewardSatoshis <= 0)
            {
                _logger.LogError("Failed to convert reward amount {Amount} {Currency} to satoshis for store {StoreId}", 
                    rewardAmount, transaction.Currency, storeId);
                return false;
            }

            // Apply maximum reward cap if set
            if (settings.MaximumRewardSatoshis.HasValue && 
                rewardSatoshis > settings.MaximumRewardSatoshis.Value)
            {
                rewardSatoshis = settings.MaximumRewardSatoshis.Value;
                // Recalculate rewardAmount in original currency based on capped satoshis
                var btcAmount = rewardSatoshis / 100_000_000m;
                var btcRate = await GetBtcRateAsync(transaction.Currency);
                if (btcRate.HasValue)
                {
                    var cappedBtc = rewardSatoshis / 100_000_000m;
                    rewardAmount = cappedBtc * btcRate.Value;
                }
            }

            // Create reward record
            var reward = new BitcoinRewardRecord
            {
                StoreId = storeId,
                Platform = platformEnum,
                TransactionId = transaction.TransactionId,
                OrderId = transaction.OrderId,
                CustomerEmail = transaction.CustomerEmail,
                CustomerPhone = transaction.CustomerPhone,
                TransactionAmount = transaction.Amount,
                Currency = transaction.Currency,
                RewardAmount = rewardAmount,
                RewardAmountSatoshis = rewardSatoshis,
                Status = RewardStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            // Attempt to create a native pull payment first
            var pullPaymentResult = await _pullPaymentService.CreatePullPaymentAsync(
                storeId,
                rewardSatoshis,
                settings.SelectedPayoutProcessorId,
                transaction.OrderId ?? transaction.TransactionId,
                CancellationToken.None);

            if (pullPaymentResult.Success && !string.IsNullOrEmpty(pullPaymentResult.PullPaymentId))
            {
                reward.PullPaymentId = pullPaymentResult.PullPaymentId;
                reward.PayoutProcessor = pullPaymentResult.PayoutProcessor;
                reward.PayoutMethod = pullPaymentResult.PayoutMethod;
                reward.ClaimLink = pullPaymentResult.ClaimLink;
                reward.Status = RewardStatus.Sent;
                reward.SentAt = DateTime.UtcNow;

                var deliveryTarget = settings.DeliveryMethod == DeliveryMethod.Email
                    ? transaction.CustomerEmail
                    : transaction.CustomerPhone;

                await _repository.AddRewardAsync(reward);

                var hasEmailOrPhone = !string.IsNullOrEmpty(deliveryTarget);

                _logger.LogInformation("Reward created for store {StoreId}, transaction {TransactionId}, order {OrderId} - HasEmailOrPhone: {HasEmailOrPhone}, DeliveryTarget: '{DeliveryTarget}', DeliveryMethod: {DeliveryMethod}",
                    storeId, transaction.TransactionId, transaction.OrderId ?? transaction.TransactionId, hasEmailOrPhone, deliveryTarget, settings.DeliveryMethod);

                if (hasEmailOrPhone)
                {
                    var rewardAmountBtc = rewardSatoshis / 100_000_000m;
                    var sent = await _emailService.SendRewardNotificationAsync(
                        deliveryTarget,
                        settings.DeliveryMethod,
                        rewardAmountBtc,
                        rewardSatoshis,
                        pullPaymentResult.ClaimLink,
                        storeId,
                        transaction.OrderId ?? transaction.TransactionId,
                        settings.EmailTemplate,
                        settings.EmailSubject);

                    _logger.LogInformation("Email notification result for store {StoreId}, transaction {TransactionId} - Sent: {Sent}", storeId, transaction.TransactionId, sent);

                    if (!sent)
                    {
                        reward.ErrorMessage = "Reward created but email notification failed (email plugin not available or send error)";
                        await _repository.UpdateRewardAsync(reward);
                    }
                }
                else
                {
                    _logger.LogInformation("Skipping email notification for store {StoreId}, transaction {TransactionId} - no delivery target available", storeId, transaction.TransactionId);
                }

                _logger.LogInformation("Reward pull payment created for store {StoreId} with pull payment {PullPaymentId}", storeId, pullPaymentResult.PullPaymentId);
                return true;
            }
            else if (!pullPaymentResult.Success && !string.IsNullOrEmpty(pullPaymentResult.Error))
            {
                reward.ErrorMessage = pullPaymentResult.Error;
                _logger.LogWarning("Pull payment creation failed for store {StoreId}: {Error}", storeId, pullPaymentResult.Error);
            }

            // Pull payment failed: persist record with pending status and error for visibility
            reward.Status = RewardStatus.Pending;
            await _repository.AddRewardAsync(reward);
            _logger.LogWarning("Reward stored as pending due to pull payment failure for transaction {TransactionId} in store {StoreId}", 
                transaction.TransactionId, storeId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing reward for transaction {TransactionId} in store {StoreId}", 
                transaction.TransactionId, storeId);
            return false;
        }
    }

    public decimal CalculateRewardAmount(decimal transactionAmount, decimal percentage)
    {
        return transactionAmount * (percentage / 100m);
    }

    private async Task<decimal?> GetBtcRateAsync(string fromCurrency)
    {
        if (string.IsNullOrWhiteSpace(fromCurrency))
            return null;

        try
        {
            var currency = fromCurrency.Trim().ToUpperInvariant();
            
            // BTC to BTC is always 1
            if (currency == "BTC")
                return 1.0m;
            
            // Use BTCPay Server's rate fetcher with configured rate providers
            var pair = new CurrencyPair("BTC", currency);
            
            // Try multiple rate providers: kraken, coinbase, bitpay as fallbacks
            // Format: X_X = provider1(X_X), provider2(X_X), provider3(X_X);
            RateRules rules;
            if (!RateRules.TryParse($"X_X = kraken(X_X), coinbasepro(X_X), bitpay(X_X);", out rules))
            {
                _logger.LogWarning("Failed to parse rate rules for {Currency}", currency);
                return null;
            }
            
            var rateResult = await _rateFetcher.FetchRate(pair, rules, null, CancellationToken.None);
            
            if (rateResult?.BidAsk?.Bid != null && rateResult.BidAsk.Bid > 0)
            {
                return rateResult.BidAsk.Bid;
            }

            if (rateResult?.ExchangeExceptions?.Any() == true)
            {
                foreach (var ex in rateResult.ExchangeExceptions)
                {
                    _logger.LogWarning("Rate fetch failed for {Currency} from {Exchange}: {Error}", 
                        currency, ex.ExchangeName, ex.Exception?.Message ?? "Unknown error");
                }
            }
            
            _logger.LogWarning("No BTC rate available for currency {Currency}", currency);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching rate for {Currency}", fromCurrency);
            return null;
        }
    }

    private async Task<long> ConvertToSatoshisAsync(string fromCurrency, decimal amount, string storeId)
    {
        try
        {
            var currency = string.IsNullOrWhiteSpace(fromCurrency)
                ? "BTC"
                : fromCurrency.Trim().ToUpperInvariant();

            static long EnsureNonNegative(long sats) => Math.Max(0, sats);

            long satoshis;

            // Native sat and btc handling (no fiat conversion needed)
            if (currency is "SAT" or "SATS" or "SATOSHI" or "SATOSHIS")
            {
                satoshis = EnsureNonNegative((long)Math.Round(amount, MidpointRounding.AwayFromZero));
                return satoshis;
            }

            if (currency is "MSAT" or "MSATS")
            {
                satoshis = EnsureNonNegative((long)Math.Round(amount / 1000m, MidpointRounding.AwayFromZero));
                return satoshis;
            }

            if (currency == "BTC")
            {
                satoshis = EnsureNonNegative((long)Math.Round(amount * 100_000_000m, MidpointRounding.AwayFromZero));
                return satoshis;
            }

            var rate = await GetBtcRateAsync(currency);
            if (!rate.HasValue || rate.Value <= 0)
            {
                _logger.LogWarning("No BTC rate available for currency {Currency}", currency);
                return 0L;
            }

            var btcAmount = amount / rate.Value;
            satoshis = EnsureNonNegative((long)Math.Round(btcAmount * 100_000_000m, MidpointRounding.AwayFromZero));

            // Enforce minimum 1 sat when amount > 0 to avoid zeroed rewards
            if (satoshis == 0 && amount > 0)
            {
                satoshis = 1;
            }

            return satoshis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting {Amount} {Currency} to satoshis", amount, fromCurrency);
            return 0L;
        }
    }

    private async Task<decimal> ConvertToStoreCurrencyAsync(decimal amount, string fromCurrency, string storeCurrency)
    {
        var fromCur = string.IsNullOrWhiteSpace(fromCurrency) ? "BTC" : fromCurrency.Trim().ToUpperInvariant();
        var toCur = string.IsNullOrWhiteSpace(storeCurrency) ? "BTC" : storeCurrency.Trim().ToUpperInvariant();

        if (fromCur == toCur)
            return amount;

        try
        {
            // Handle sats/msats target directly
            if (toCur is "SAT" or "SATS" or "SATOSHI" or "SATOSHIS")
            {
                // Convert from to BTC then to sats
                var rateFrom = await GetBtcRateAsync(fromCur);
                if (!rateFrom.HasValue || rateFrom.Value <= 0)
                    return 0m;
                var btcAmount = amount / rateFrom.Value;
                return btcAmount * 100_000_000m;
            }

            // Convert via BTC bridge for fiat->fiat or fiat->BTC/BTC->fiat
            var rateFromFiatPerBtc = await GetBtcRateAsync(fromCur);
            var rateToFiatPerBtc = await GetBtcRateAsync(toCur);
            if (!rateFromFiatPerBtc.HasValue || rateFromFiatPerBtc.Value <= 0 || !rateToFiatPerBtc.HasValue || rateToFiatPerBtc.Value <= 0)
            {
                _logger.LogWarning("Missing rate to convert {Amount} {From} to {To}", amount, fromCur, toCur);
                return 0m;
            }

            var btcAmountBridge = amount / rateFromFiatPerBtc.Value;
            var targetAmount = btcAmountBridge * rateToFiatPerBtc.Value;
            return targetAmount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting {Amount} from {FromCurrency} to store currency {StoreCurrency}", amount, fromCurrency, storeCurrency);
            return 0m;
        }
    }
}

