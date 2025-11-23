using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Plugins.BitcoinRewards.Repositories;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace BTCPayServer.Plugins.BitcoinRewards
{
    public class BitcoinRewardsService : EventHostedServiceBase
    {
        private readonly StoreRepository _storeRepository;
        private readonly RewardRecordRepository _rewardRepository;
        private readonly WalletService _walletService;
        private readonly EmailService _emailService;
        private readonly RateService _rateService;

        public BitcoinRewardsService(
            EventAggregator eventAggregator,
            StoreRepository storeRepository,
            RewardRecordRepository rewardRepository,
            WalletService walletService,
            EmailService emailService,
            RateService rateService,
            Logs logs) : base(eventAggregator, logs)
        {
            _storeRepository = storeRepository;
            _rewardRepository = rewardRepository;
            _walletService = walletService;
            _emailService = emailService;
            _rateService = rateService;
        }

        protected override void SubscribeToEvents()
        {
            // Subscribe to invoice events if needed
            base.SubscribeToEvents();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            // Process events if needed
            await base.ProcessEvent(evt, cancellationToken);
        }

        public async Task<RewardRecord> ProcessOrderReward(OrderData orderData)
        {
            var store = await _storeRepository.FindStore(orderData.StoreId);
            if (store == null)
            {
                throw new Exception($"Store {orderData.StoreId} not found");
            }

            var settings = BitcoinRewards.BitcoinRewardsExtensions.GetBitcoinRewardsSettings(store.GetStoreBlob());
            if (settings == null || !settings.Enabled)
            {
                throw new Exception("Bitcoin Rewards is not enabled for this store");
            }

            // Check if customer has email or phone
            if (string.IsNullOrEmpty(orderData.CustomerEmail) && string.IsNullOrEmpty(orderData.CustomerPhone))
            {
                throw new Exception("Customer must have either email or phone number to receive rewards");
            }

            // Check minimum order amount
            if (orderData.OrderAmount < settings.MinimumOrderAmount)
            {
                throw new Exception($"Order amount {orderData.OrderAmount} is below minimum {settings.MinimumOrderAmount}");
            }

            // Calculate reward amount in fiat
            var rewardAmountFiat = orderData.OrderAmount * settings.RewardPercentage;
            if (rewardAmountFiat > settings.MaximumRewardAmount)
            {
                rewardAmountFiat = settings.MaximumRewardAmount;
            }

            // Convert to BTC using rate service
            var rewardAmountBTC = await _rateService.ConvertToBTC(
                rewardAmountFiat, 
                orderData.Currency ?? "USD",
                settings.PreferredExchangeRateProvider);

            var rewardRecord = new RewardRecord
            {
                Id = Guid.NewGuid().ToString(),
                OrderId = orderData.OrderId,
                StoreId = orderData.StoreId,
                CustomerEmail = orderData.CustomerEmail,
                CustomerPhone = orderData.CustomerPhone,
                RewardAmount = rewardAmountBTC,
                Status = RewardStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                Source = orderData.Source
            };

            // Save to database
            await _rewardRepository.CreateAsync(rewardRecord);

            // Process reward asynchronously
            _ = Task.Run(async () => await ProcessRewardAsync(rewardRecord, settings, store));

            return rewardRecord;
        }

        private async Task ProcessRewardAsync(RewardRecord rewardRecord, BitcoinRewardsSettings settings, StoreData store)
        {
            const int maxRetries = 3;
            int retryCount = 0;

            while (retryCount < maxRetries)
            {
                try
                {
                    rewardRecord.Status = RewardStatus.Processing;
                    await _rewardRepository.UpdateAsync(rewardRecord);

                    // Get or generate Bitcoin address for customer
                    string destinationAddress = null;
                    
                    // Try to reuse address from previous rewards
                    if (!string.IsNullOrEmpty(rewardRecord.CustomerEmail))
                    {
                        var previousReward = await _rewardRepository.GetByCustomerEmailAsync(
                            rewardRecord.CustomerEmail, 
                            rewardRecord.StoreId);
                        
                        if (previousReward != null && !string.IsNullOrEmpty(previousReward.BitcoinAddress))
                        {
                            destinationAddress = previousReward.BitcoinAddress;
                            Logs.PayServer.LogInformation($"Reusing address {destinationAddress} for customer {rewardRecord.CustomerEmail}");
                        }
                    }

                    // Generate new address if needed
                    // Note: In production, this should use BTCPay Server's wallet service
                    // For now, we'll require addresses to be provided or generated by the wallet service
                    if (string.IsNullOrEmpty(destinationAddress))
                    {
                        destinationAddress = _walletService.GenerateAddress(store);
                        if (string.IsNullOrEmpty(destinationAddress))
                        {
                            // If address generation fails, we'll need to handle this
                            // In a production system, you might want to:
                            // 1. Use a customer-provided address from settings
                            // 2. Generate using BTCPay Server's actual wallet API
                            // 3. Create a Lightning invoice instead
                            Logs.PayServer.LogWarning($"Address generation not available for reward {rewardRecord.Id}, using placeholder");
                            destinationAddress = "address-generation-required"; // Placeholder
                        }
                        else
                        {
                            Logs.PayServer.LogInformation($"Generated new address {destinationAddress} for reward {rewardRecord.Id}");
                        }
                    }

                    rewardRecord.BitcoinAddress = destinationAddress;
                    await _rewardRepository.UpdateAsync(rewardRecord);

                    // Send Bitcoin using wallet service
                    var sendResult = await _walletService.SendBitcoinReward(
                        store,
                        rewardRecord.RewardAmount,
                        destinationAddress,
                        settings.WalletPreference);

                    if (!sendResult.Success)
                    {
                        throw new Exception($"Failed to send Bitcoin: {sendResult.Error}");
                    }

                    rewardRecord.TransactionId = sendResult.TransactionId;
                    rewardRecord.Status = RewardStatus.Sent;
                    rewardRecord.SentAt = DateTime.UtcNow;
                    await _rewardRepository.UpdateAsync(rewardRecord);

                    Logs.PayServer.LogInformation($"Successfully sent reward {rewardRecord.Id} via {sendResult.PaymentMethod}");

                    // Send email notification
                    await _emailService.SendRewardEmailAsync(rewardRecord, settings, store);

                    return; // Success, exit retry loop
                }
                catch (Exception ex)
                {
                    retryCount++;
                    Logs.PayServer.LogError(ex, $"Failed to process reward {rewardRecord.Id} (attempt {retryCount}/{maxRetries}): {ex.Message}");

                    if (retryCount >= maxRetries)
                    {
                        rewardRecord.Status = RewardStatus.Failed;
                        await _rewardRepository.UpdateAsync(rewardRecord);
                        Logs.PayServer.LogError($"Reward {rewardRecord.Id} failed after {maxRetries} attempts");
                    }
                    else
                    {
                        // Wait before retrying (exponential backoff)
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)));
                    }
                }
            }
        }

        public async Task<RewardRecord> GetReward(string rewardId)
        {
            return await _rewardRepository.GetByIdAsync(rewardId);
        }

        public async Task<List<RewardRecord>> GetRewardsForStore(string storeId)
        {
            return await _rewardRepository.GetByStoreIdAsync(storeId);
        }
    }
}

