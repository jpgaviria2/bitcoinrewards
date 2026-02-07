#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using BTCPayServer.Data;
using System.Linq;
using BTCPayServer.Services.Rates;
using BTCPayServer.Rating;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

public class BitcoinRewardsService
{
    // Constants
    private const decimal SATS_PER_BTC = 100_000_000m;
    private const decimal MSATS_PER_SAT = 1000m;
    private const long MIN_SATOSHIS = 1L;

    // Security: PII masking for logs
    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email))
            return "***";
        
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0)
            return "***";
        
        var localPart = email.Substring(0, atIndex);
        var domain = email.Substring(atIndex);
        
        if (localPart.Length <= 2)
            return $"**{domain}";
        
        return $"{localPart[0]}***{localPart[^1]}{domain}";
    }
    
    private static string MaskPhone(string? phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 4)
            return "***";
        
        return $"***{phone.Substring(phone.Length - 4)}";
    }
    
    private readonly StoreRepository _storeRepository;
    private readonly BitcoinRewardsRepository _repository;
    private readonly IEmailNotificationService _emailService;
    private readonly RewardPullPaymentService _pullPaymentService;
    private readonly ILogger<BitcoinRewardsService> _logger;
    private readonly RateFetcher _rateFetcher;
    private readonly DefaultRulesCollection _defaultRules;

    public BitcoinRewardsService(
        StoreRepository storeRepository,
        BitcoinRewardsRepository repository,
        IEmailNotificationService emailService,
        ILogger<BitcoinRewardsService> logger,
        RewardPullPaymentService pullPaymentService,
        RateFetcher rateFetcher,
        DefaultRulesCollection defaultRules)
    {
        _storeRepository = storeRepository;
        _repository = repository;
        _emailService = emailService;
        _pullPaymentService = pullPaymentService;
        _logger = logger;
        _rateFetcher = rateFetcher;
        _defaultRules = defaultRules;
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

            // Check if platform is enabled - fail-secure: reject if no platforms are explicitly enabled
            if (settings.EnabledPlatforms == PlatformFlags.None)
            {
                _logger.LogWarning("No platforms enabled for store {StoreId}, rejecting transaction", storeId);
                return false;
            }
            
            if (settings.EnabledPlatforms != PlatformFlags.None)
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
            var txAmountInStoreCurrency = await ConvertToStoreCurrencyAsync(transaction.Amount, transaction.Currency, storeCurrency, storeId);

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

            // Calculate reward amount using configured percentages
            decimal rewardAmount;
            var percentage = transaction.Platform == TransactionPlatform.Btcpay
                ? settings.BtcpayRewardPercentage
                : settings.ExternalRewardPercentage > 0 ? settings.ExternalRewardPercentage : settings.RewardPercentage;
            rewardAmount = CalculateRewardAmount(transaction.Amount, percentage);
            _logger.LogInformation("Reward calculation for {Platform} transaction {TransactionId}: Amount={Amount} {Currency}, Percentage={Percentage}%, RewardAmount={RewardAmount}",
                transaction.Platform, transaction.TransactionId, transaction.Amount, transaction.Currency, percentage, rewardAmount);

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

            // Security: Enforce maximum single reward cap to prevent fraudulent large rewards
            if (rewardSatoshis > settings.MaximumSingleRewardSatoshis)
            {
                _logger.LogWarning("🚨 SECURITY: Reward {RewardSats} sats exceeds maximum single reward cap {MaxSats} for store {StoreId}, transaction {TxId}. Rejecting reward.",
                    rewardSatoshis, settings.MaximumSingleRewardSatoshis, storeId, transaction.TransactionId);
                return false;
            }

            // Apply maximum reward cap if set (for backward compatibility)
            if (settings.MaximumRewardSatoshis.HasValue && 
                rewardSatoshis > settings.MaximumRewardSatoshis.Value)
            {
                rewardSatoshis = settings.MaximumRewardSatoshis.Value;
                // Recalculate rewardAmount in original currency based on capped satoshis
                var btcAmount = rewardSatoshis / 100_000_000m;
                var btcRate = await GetBtcRateAsync(transaction.Currency, storeId);
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

            // Security: Save reward to database - unique constraint will prevent duplicates
            try
            {
                await _repository.AddRewardAsync(reward);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) 
                when (ex.InnerException?.Message?.Contains("duplicate key") == true ||
                      ex.InnerException?.Message?.Contains("UNIQUE constraint") == true)
            {
                _logger.LogWarning("🚨 SECURITY: Duplicate transaction {TransactionId} blocked by database constraint for store {StoreId} (concurrent request)",
                    transaction.TransactionId, storeId);
                return false;
            }
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

                // Security: Mask PII in logs
                var maskedTarget = settings.DeliveryMethod == DeliveryMethod.Email 
                    ? MaskEmail(deliveryTarget) 
                    : MaskPhone(deliveryTarget);
                
                _logger.LogInformation("Reward created for store {StoreId}, transaction {TransactionId}, order {OrderId} - HasEmailOrPhone: {HasEmailOrPhone}, DeliveryTarget: '{MaskedTarget}', DeliveryMethod: {DeliveryMethod}",
                    storeId, transaction.TransactionId, transaction.OrderId ?? transaction.TransactionId, hasEmailOrPhone, maskedTarget, settings.DeliveryMethod);

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

    private async Task<decimal?> GetBtcRateAsync(string fromCurrency, string storeId)
    {
        _logger.LogDebug("[RATE FETCH] ENTRY: Currency={Currency}, StoreId={StoreId}", fromCurrency, storeId);
        
        if (string.IsNullOrWhiteSpace(fromCurrency))
        {
            _logger.LogWarning("[RATE FETCH] Currency is null or whitespace");
            return null;
        }

        try
        {
            var currency = fromCurrency.Trim().ToUpperInvariant();
            _logger.LogDebug("[RATE FETCH] Normalized currency: {Currency}", currency);
            
            // BTC to BTC is always 1
            if (currency == "BTC")
                return 1.0m;
            
            // Get store to access rate configuration
            var store = await _storeRepository.FindStore(storeId);
            if (store == null)
            {
                _logger.LogWarning("Store {StoreId} not found for rate fetch", storeId);
                return null;
            }

            var storeBlob = store.GetStoreBlob();
            var rulesCollection = storeBlob.GetRateRules(_defaultRules);
            
            // Fetch rate for BTC to target currency pair
            var currencyPair = new CurrencyPair("BTC", currency);
            
            _logger.LogDebug("[RATE FETCH] Store {StoreId}: Fetching {CurrencyPair}", storeId, currencyPair);
            _logger.LogDebug("[RATE FETCH] Primary rules: {Primary}", rulesCollection.Primary?.ToString() ?? "NULL");
            _logger.LogDebug("[RATE FETCH] Fallback rules: {Fallback}", rulesCollection.Fallback?.ToString() ?? "NULL");
            _logger.LogDebug("[RATE FETCH] Spread: {Spread}%", storeBlob.Spread);

            var rateResult = await _rateFetcher.FetchRate(
                currencyPair,
                rulesCollection,
                new StoreIdRateContext(storeId),
                CancellationToken.None
            );
            
            _logger.LogDebug("[RATE FETCH] Result received: BidAsk={BidAsk}, ErrorCount={ErrorCount}, ExceptionCount={ExceptionCount}",
                rateResult?.BidAsk?.Bid, rateResult?.Errors?.Count ?? 0, rateResult?.ExchangeExceptions?.Count ?? 0);

            if (rateResult?.BidAsk?.Bid != null && rateResult.BidAsk.Bid > 0)
            {
                _logger.LogInformation("[RATE FETCH] ✅ SUCCESS: {CurrencyPair} = {Rate} (Rule: {Rule})",
                    currencyPair, rateResult.BidAsk.Bid, rateResult.EvaluatedRule);
                return rateResult.BidAsk.Bid;
            }

            // Log detailed error information
            _logger.LogWarning("[RATE FETCH] ❌ FAILED for {CurrencyPair}", currencyPair);
            
            if (rateResult != null)
            {
                if (rateResult.Errors?.Count > 0)
                {
                    var errors = string.Join(", ", rateResult.Errors);
                    _logger.LogWarning("[RATE FETCH] Errors: {Errors}", errors);
                }
                else
                {
                    _logger.LogWarning("[RATE FETCH] No errors reported but BidAsk is null or zero");
                }
                
                if (rateResult.ExchangeExceptions?.Count > 0)
                {
                    _logger.LogWarning("[RATE FETCH] Exchange exceptions count: {Count}", rateResult.ExchangeExceptions.Count);
                    foreach (var exc in rateResult.ExchangeExceptions)
                    {
                        _logger.LogWarning("[RATE FETCH] Exchange '{Exchange}': {Message}",
                            exc.ExchangeName, exc.Exception?.Message ?? "Unknown error");
                    }
                }
                else
                {
                    _logger.LogWarning("[RATE FETCH] No exchange exceptions reported");
                }
                
                _logger.LogWarning("[RATE FETCH] Rule used: {Rule}", rateResult.Rule ?? "NULL");
                _logger.LogWarning("[RATE FETCH] Evaluated rule: {EvaluatedRule}", rateResult.EvaluatedRule ?? "NULL");
            }
            else
            {
                _logger.LogWarning("[RATE FETCH] RateResult is completely NULL - RateFetcher may not be working!");
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching BTC rate for {Currency} in store {StoreId}", 
                fromCurrency, storeId);
            return null;
        }
    }

    private async Task<long> ConvertToSatoshisAsync(string fromCurrency, decimal amount, string storeId)
    {
        try
        {
            // Security: Validate input range before conversion to prevent integer overflow
            const decimal MAX_BTC_AMOUNT = 21_000_000m; // Total BTC supply
            const decimal MAX_SATS_DECIMAL = 2_100_000_000_000_000m; // 21M BTC in sats
            
            if (amount < 0)
            {
                _logger.LogWarning("🚨 SECURITY: Negative amount {Amount} {Currency} rejected for store {StoreId}", 
                    amount, fromCurrency, storeId);
                return 0L;
            }
            
            var currency = string.IsNullOrWhiteSpace(fromCurrency)
                ? "BTC"
                : fromCurrency.Trim().ToUpperInvariant();

            static long EnsureNonNegative(long sats) => Math.Max(0, sats);

            long satoshis;

            // Native sat and btc handling (no fiat conversion needed)
            if (currency is "SAT" or "SATS" or "SATOSHI" or "SATOSHIS")
            {
                if (amount > MAX_SATS_DECIMAL)
                {
                    _logger.LogWarning("🚨 SECURITY: Excessive satoshi amount {Amount} rejected for store {StoreId}", 
                        amount, storeId);
                    return 0L;
                }
                satoshis = EnsureNonNegative((long)Math.Round(amount, MidpointRounding.AwayFromZero));
                return satoshis;
            }

            if (currency is "MSAT" or "MSATS")
            {
                satoshis = EnsureNonNegative((long)Math.Round(amount / MSATS_PER_SAT, MidpointRounding.AwayFromZero));
                return satoshis;
            }

            if (currency == "BTC")
            {
                if (amount > MAX_BTC_AMOUNT)
                {
                    _logger.LogWarning("🚨 SECURITY: Excessive BTC amount {Amount} rejected for store {StoreId}", 
                        amount, storeId);
                    return 0L;
                }
                
                var satsDecimal = amount * SATS_PER_BTC;
                
                // Check before casting to long to prevent overflow
                if (satsDecimal > long.MaxValue || satsDecimal < 0)
                {
                    _logger.LogWarning("🚨 SECURITY: Satoshi overflow detected for {Amount} BTC in store {StoreId}", 
                        amount, storeId);
                    return 0L;
                }
                
                checked
                {
                    satoshis = EnsureNonNegative((long)Math.Round(satsDecimal, MidpointRounding.AwayFromZero));
                }
                return satoshis;
            }

            var rate = await GetBtcRateAsync(currency, storeId);
            if (!rate.HasValue || rate.Value <= 0)
            {
                _logger.LogWarning("No BTC rate available for currency {Currency}", currency);
                return 0L;
            }

            var btcAmount = amount / rate.Value;
            
            // Security: Validate BTC amount before conversion
            if (btcAmount > MAX_BTC_AMOUNT)
            {
                _logger.LogWarning("🚨 SECURITY: Fiat conversion resulted in excessive BTC amount {Amount} for store {StoreId}", 
                    btcAmount, storeId);
                return 0L;
            }
            
            var fiatSatsDecimal = btcAmount * SATS_PER_BTC;
            
            if (fiatSatsDecimal > long.MaxValue || fiatSatsDecimal < 0)
            {
                _logger.LogWarning("🚨 SECURITY: Satoshi overflow in fiat conversion for {Amount} {Currency} in store {StoreId}", 
                    amount, currency, storeId);
                return 0L;
            }
            
            checked
            {
                satoshis = EnsureNonNegative((long)Math.Round(fiatSatsDecimal, MidpointRounding.AwayFromZero));
            }

            // Enforce minimum 1 sat when amount > 0 to avoid zeroed rewards
            if (satoshis == 0 && amount > 0)
            {
                satoshis = MIN_SATOSHIS;
            }

            return satoshis;
        }
        catch (OverflowException ex)
        {
            _logger.LogError(ex, "🚨 SECURITY: Integer overflow in satoshi conversion for {Amount} {Currency}", 
                amount, fromCurrency);
            return 0L;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting {Amount} {Currency} to satoshis", amount, fromCurrency);
            return 0L;
        }
    }

    private async Task<decimal> ConvertToStoreCurrencyAsync(decimal amount, string fromCurrency, string storeCurrency, string storeId)
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
                var rateFrom = await GetBtcRateAsync(fromCur, storeId);
                if (!rateFrom.HasValue || rateFrom.Value <= 0)
                    return 0m;
                var btcAmount = amount / rateFrom.Value;
                return btcAmount * 100_000_000m;
            }

            // Convert via BTC bridge for fiat->fiat or fiat->BTC/BTC->fiat
            var rateFromFiatPerBtc = await GetBtcRateAsync(fromCur, storeId);
            var rateToFiatPerBtc = await GetBtcRateAsync(toCur, storeId);
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

