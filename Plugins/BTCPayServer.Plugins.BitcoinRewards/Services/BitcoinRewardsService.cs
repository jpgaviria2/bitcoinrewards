#nullable enable
using System;
using System.Threading.Tasks;
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
    private readonly ICashuService _cashuService;
    private readonly IEmailNotificationService _emailService;
    private readonly CurrencyNameTable _currencyNameTable;
    private readonly ILogger<BitcoinRewardsService> _logger;

    public BitcoinRewardsService(
        StoreRepository storeRepository,
        BitcoinRewardsRepository repository,
        ICashuService cashuService,
        IEmailNotificationService emailService,
        CurrencyNameTable currencyNameTable,
        ILogger<BitcoinRewardsService> logger)
    {
        _storeRepository = storeRepository;
        _repository = repository;
        _cashuService = cashuService;
        _emailService = emailService;
        _currencyNameTable = currencyNameTable;
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
            var platform = transaction.Platform == TransactionPlatform.Shopify 
                ? PlatformFlags.Shopify 
                : PlatformFlags.Square;
            
            if ((settings.EnabledPlatforms & platform) == PlatformFlags.None)
            {
                _logger.LogDebug("Platform {Platform} not enabled for store {StoreId}", transaction.Platform, storeId);
                return false;
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
            
            // Convert to satoshis (simplified - assumes 1 BTC = 100,000,000 sats)
            // TODO: Use proper currency conversion service
            var rewardSatoshis = ConvertToSatoshis(rewardAmount);

            // Apply maximum reward cap if set
            if (settings.MaximumRewardSatoshis.HasValue && 
                rewardSatoshis > settings.MaximumRewardSatoshis.Value)
            {
                rewardSatoshis = settings.MaximumRewardSatoshis.Value;
                rewardAmount = rewardSatoshis / 100_000_000m;
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

    public long ConvertToSatoshis(decimal btcAmount)
    {
        // Simplified conversion - assumes 1 BTC = 100,000,000 sats
        // TODO: Use proper rate service for currency conversion
        return (long)(btcAmount * 100_000_000m);
    }
}

