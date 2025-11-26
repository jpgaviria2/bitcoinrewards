#nullable enable
using System;
using System.Threading.Tasks;
using System.Threading;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

public class BitcoinRewardsService
{
    private readonly StoreRepository _storeRepository;
    private readonly BitcoinRewardsRepository _repository;
    private readonly ICashuService _cashuService;
    private readonly IEmailNotificationService _emailService;
    private readonly CurrencyNameTable _currencyNameTable;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<BitcoinRewardsService> _logger;

    public BitcoinRewardsService(
        StoreRepository storeRepository,
        BitcoinRewardsRepository repository,
        ICashuService cashuService,
        IEmailNotificationService emailService,
        CurrencyNameTable currencyNameTable,
        ILogger<BitcoinRewardsService> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _storeRepository = storeRepository;
        _repository = repository;
        _cashuService = cashuService;
        _emailService = emailService;
        _currencyNameTable = currencyNameTable;
        _httpContextAccessor = httpContextAccessor;
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

            // Calculate reward amount
            var rewardAmount = CalculateRewardAmount(transaction.Amount, settings.RewardPercentage);
            
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

            // Generate ecash token
            var ecashToken = await _cashuService.MintTokenAsync(rewardSatoshis, storeId);
            if (string.IsNullOrEmpty(ecashToken))
            {
                reward.Status = RewardStatus.Pending;
                reward.ErrorMessage = "Failed to generate ecash token";
                await _repository.AddRewardAsync(reward);
                _logger.LogError("Failed to mint ecash token for reward {RewardId}", reward.Id);
                return false;
            }

            reward.EcashToken = ecashToken;

            // Send notification
            var deliveryMethod = settings.DeliveryMethod == DeliveryMethod.Email 
                ? transaction.CustomerEmail 
                : transaction.CustomerPhone;
            
            if (!string.IsNullOrEmpty(deliveryMethod))
            {
                var sent = await _emailService.SendRewardNotificationAsync(
                    deliveryMethod,
                    settings.DeliveryMethod,
                    rewardAmount,
                    rewardSatoshis,
                    ecashToken,
                    transaction.OrderId ?? transaction.TransactionId);

                if (sent)
                {
                    reward.Status = RewardStatus.Sent;
                    reward.SentAt = DateTime.UtcNow;
                }
                else
                {
                    reward.Status = RewardStatus.Pending;
                    reward.ErrorMessage = "Failed to send notification";
                }
            }
            else
            {
                reward.Status = RewardStatus.Sent; // No contact info, but token generated
                reward.ErrorMessage = "No customer email/phone provided";
            }

            await _repository.AddRewardAsync(reward);
            _logger.LogInformation("Reward processed successfully for transaction {TransactionId} in store {StoreId}", 
                transaction.TransactionId, storeId);
            
            return true;
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
            // Simplified conversion for now
            // For manual testing, we'll use a fixed rate approximation
            // TODO: Integrate with actual BTCPay Server rate service
            
            // Approximate conversion: assume $50k per BTC if USD, otherwise use a rough estimate
            decimal btcPrice;
            if (fromCurrency.Equals("USD", StringComparison.OrdinalIgnoreCase))
            {
                btcPrice = 50000m; // Approximate BTC price in USD
            }
            else if (fromCurrency.Equals("EUR", StringComparison.OrdinalIgnoreCase))
            {
                btcPrice = 45000m; // Rough EUR estimate
            }
            else
            {
                // For other currencies, use a conservative estimate
                btcPrice = 50000m;
                _logger.LogWarning("Using default BTC price for currency {Currency}", fromCurrency);
            }

            // Convert fiat amount to BTC
            var btcAmount = amount / btcPrice;
            
            // Convert BTC to satoshis (1 BTC = 100,000,000 sats)
            var satoshis = (long)(btcAmount * 100_000_000m);
            
            // Ensure minimum of 1 satoshi
            return Task.FromResult(Math.Max(1, satoshis));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting {Amount} {Currency} to satoshis", amount, fromCurrency);
            return Task.FromResult(0L);
        }
    }
}

