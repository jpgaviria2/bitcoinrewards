#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

public class BitcoinRewardsService
{
    private readonly StoreRepository _storeRepository;
    private readonly BitcoinRewardsRepository _repository;
    private readonly IEmailNotificationService _emailService;
    private readonly CurrencyNameTable _currencyNameTable;
    private readonly RewardPullPaymentService _pullPaymentService;
    private readonly ILogger<BitcoinRewardsService> _logger;

    public BitcoinRewardsService(
        StoreRepository storeRepository,
        BitcoinRewardsRepository repository,
        IEmailNotificationService emailService,
        CurrencyNameTable currencyNameTable,
        ILogger<BitcoinRewardsService> logger,
        RewardPullPaymentService pullPaymentService)
    {
        _storeRepository = storeRepository;
        _repository = repository;
        _emailService = emailService;
        _currencyNameTable = currencyNameTable;
        _pullPaymentService = pullPaymentService;
        _logger = logger;
    }

    public async Task<bool> ProcessRewardAsync(string storeId, TransactionData transaction)
    {
        try
        {
            // Get store settings
            var settings = await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(
                storeId, 
                BitcoinRewardsStoreSettings.SettingsName);

            if (settings == null || !settings.Enabled)
            {
                _logger.LogDebug("Bitcoin Rewards not enabled for store {StoreId}", storeId);
                return false;
            }

            // Check if platform is enabled
            // For manual test rewards, skip platform validation if no platform flags are set
            if (settings.EnabledPlatforms != PlatformFlags.None)
            {
                var platform = transaction.Platform == TransactionPlatform.Shopify 
                    ? PlatformFlags.Shopify 
                    : PlatformFlags.Square;
                
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
            if (settings.MinimumTransactionAmount.HasValue && 
                transaction.Amount < settings.MinimumTransactionAmount.Value)
            {
                _logger.LogDebug("Transaction amount {Amount} below minimum {Minimum} for store {StoreId}", 
                    transaction.Amount, settings.MinimumTransactionAmount, storeId);
                return false;
            }

            // Check if transaction already processed
            var platformEnum = transaction.Platform == TransactionPlatform.Shopify 
                ? RewardPlatform.Shopify 
                : RewardPlatform.Square;
            
            if (await _repository.TransactionExistsAsync(storeId, transaction.TransactionId, platformEnum))
            {
                _logger.LogWarning("Transaction {TransactionId} already processed for store {StoreId}", 
                    transaction.TransactionId, storeId);
                return false;
            }

            var isTestReward = transaction.TransactionId.StartsWith("TEST_", StringComparison.OrdinalIgnoreCase);

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
                rewardAmount = CalculateRewardAmount(transaction.Amount, settings.RewardPercentage);
            }

            if (rewardAmount <= 0)
            {
                _logger.LogWarning("Calculated reward amount is zero or negative for transaction {TransactionId} in store {StoreId}; aborting reward creation", transaction.TransactionId, storeId);
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

                if (!string.IsNullOrEmpty(deliveryTarget))
                {
                    var rewardAmountBtc = rewardSatoshis / 100_000_000m;
                    var sent = await _emailService.SendRewardNotificationAsync(
                        deliveryTarget,
                        settings.DeliveryMethod,
                        rewardAmountBtc,
                        rewardSatoshis,
                        pullPaymentResult.ClaimLink,
                        storeId,
                        transaction.OrderId ?? transaction.TransactionId);

                    if (!sent)
                    {
                        reward.ErrorMessage = "Reward created but email notification failed (email plugin not available or send error)";
                        await _repository.UpdateRewardAsync(reward);
                    }
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

    private Task<decimal?> GetBtcRateAsync(string fromCurrency)
    {
        // Simplified for now - use approximate USD/BTC rate
        // TODO: Integrate with actual rate service
        return Task.FromResult<decimal?>(50000m); // Approximate BTC price in USD
    }

    private Task<long> ConvertToSatoshisAsync(string fromCurrency, decimal amount, string storeId)
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
                return Task.FromResult(satoshis);
            }

            if (currency is "MSAT" or "MSATS")
            {
                satoshis = EnsureNonNegative((long)Math.Round(amount / 1000m, MidpointRounding.AwayFromZero));
                return Task.FromResult(satoshis);
            }

            if (currency == "BTC")
            {
                satoshis = EnsureNonNegative((long)Math.Round(amount * 100_000_000m, MidpointRounding.AwayFromZero));
                return Task.FromResult(satoshis);
            }

            // Simplified conversion for now
            // For manual testing, we'll use a fixed rate approximation
            // TODO: Integrate with actual BTCPay Server rate service
            
            // Approximate conversion: assume $50k per BTC if USD, otherwise use a rough estimate
            decimal btcPrice;
            if (currency.Equals("USD", StringComparison.OrdinalIgnoreCase))
            {
                btcPrice = 50000m; // Approximate BTC price in USD
            }
            else if (currency.Equals("EUR", StringComparison.OrdinalIgnoreCase))
            {
                btcPrice = 45000m; // Rough EUR estimate
            }
            else
            {
                // For other currencies, use a conservative estimate
                btcPrice = 50000m;
                _logger.LogWarning("Using default BTC price for currency {Currency}", currency);
            }

            // Convert fiat amount to BTC
            var btcAmount = amount / btcPrice;
            
            // Convert BTC to satoshis (1 BTC = 100,000,000 sats)
            satoshis = EnsureNonNegative((long)Math.Round(btcAmount * 100_000_000m, MidpointRounding.AwayFromZero));
            
            return Task.FromResult(satoshis);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting {Amount} {Currency} to satoshis", amount, fromCurrency);
            return Task.FromResult(0L);
        }
    }
}

