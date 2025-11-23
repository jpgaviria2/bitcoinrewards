using System;

namespace BTCPayServer.Plugins.BitcoinRewards.Models
{
    public enum WalletPreference
    {
        LightningFirst, // Try Lightning, fallback to eCash, then on-chain
        ECashFirst,     // Try eCash, fallback to Lightning, then on-chain
        OnChainOnly     // Only use on-chain Bitcoin
    }

    public class BitcoinRewardsSettings
    {
        public bool Enabled { get; set; }
        public decimal RewardPercentage { get; set; } = 0.01m; // 1% default
        public decimal MinimumOrderAmount { get; set; } = 0m;
        public decimal MaximumRewardAmount { get; set; } = 1000m; // Maximum reward in BTC
        public bool ShopifyEnabled { get; set; }
        public bool SquareEnabled { get; set; }
        public string WebhookSecret { get; set; }
        public string EmailFromAddress { get; set; }
        public string EmailSubject { get; set; } = "Your Bitcoin Reward is Ready!";
        public string EmailBodyTemplate { get; set; } = "Congratulations! You've earned {RewardAmount} BTC as a reward for your purchase. Your reward has been sent to: {BitcoinAddress}";
        public DateTime? IntegratedAt { get; set; }
        
        // Wallet preferences
        public WalletPreference WalletPreference { get; set; } = WalletPreference.LightningFirst;
        public string PreferredLightningNodeId { get; set; }
        
        // Square API credentials
        public string SquareApplicationId { get; set; }
        public string SquareAccessToken { get; set; }
        public string SquareLocationId { get; set; }
        public string SquareEnvironment { get; set; } = "production"; // production or sandbox
        
        // Currency conversion
        public string PreferredExchangeRateProvider { get; set; } = "coingecko"; // coingecko, bitflyer, etc.
        
        public bool CredentialsPopulated()
        {
            return !string.IsNullOrEmpty(WebhookSecret);
        }
        
        public bool SquareCredentialsPopulated()
        {
            return !string.IsNullOrEmpty(SquareApplicationId) && 
                   !string.IsNullOrEmpty(SquareAccessToken) && 
                   !string.IsNullOrEmpty(SquareLocationId);
        }
    }
}

