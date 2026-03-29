using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Plugins.BitcoinRewards.Models;

namespace BTCPayServer.Plugins.BitcoinRewards.ViewModels
{
    public class RateLimitSettingsViewModel
    {
        public string StoreId { get; set; } = string.Empty;
        
        [Display(Name = "Enable Rate Limiting")]
        public bool Enabled { get; set; } = true;
        
        // Webhook policy
        [Display(Name = "Webhook Requests Per Minute")]
        [Range(1, 1000)]
        public int WebhookRequestsPerMinute { get; set; } = 60;
        
        [Display(Name = "Webhook Burst Size")]
        [Range(1, 100)]
        public int WebhookBurstSize { get; set; } = 10;
        
        // API policy
        [Display(Name = "API Requests Per Minute")]
        [Range(1, 1000)]
        public int ApiRequestsPerMinute { get; set; } = 120;
        
        [Display(Name = "API Burst Size")]
        [Range(1, 100)]
        public int ApiBurstSize { get; set; } = 20;
        
        // Admin policy
        [Display(Name = "Admin Requests Per Minute")]
        [Range(1, 1000)]
        public int AdminRequestsPerMinute { get; set; } = 300;
        
        [Display(Name = "Admin Burst Size")]
        [Range(1, 100)]
        public int AdminBurstSize { get; set; } = 50;
        
        // Store policy
        [Display(Name = "Store-wide Requests Per Minute")]
        [Range(1, 1000)]
        public int StoreRequestsPerMinute { get; set; } = 100;
        
        [Display(Name = "Store Burst Size")]
        [Range(1, 100)]
        public int StoreBurstSize { get; set; } = 20;
        
        // Whitelist/blacklist
        [Display(Name = "Whitelisted IPs (one per line)")]
        public string WhitelistedIpsText { get; set; } = string.Empty;
        
        [Display(Name = "Blacklisted IPs (one per line)")]
        public string BlacklistedIpsText { get; set; } = string.Empty;
        
        public void SetFromConfiguration(RateLimitConfiguration config)
        {
            Enabled = config.Enabled;
            WebhookRequestsPerMinute = config.WebhookPolicy.RequestsPerWindow;
            WebhookBurstSize = config.WebhookPolicy.BurstSize;
            ApiRequestsPerMinute = config.ApiPolicy.RequestsPerWindow;
            ApiBurstSize = config.ApiPolicy.BurstSize;
            AdminRequestsPerMinute = config.AdminPolicy.RequestsPerWindow;
            AdminBurstSize = config.AdminPolicy.BurstSize;
            StoreRequestsPerMinute = config.StorePolicy.RequestsPerWindow;
            StoreBurstSize = config.StorePolicy.BurstSize;
            WhitelistedIpsText = string.Join("\n", config.WhitelistedIps);
            BlacklistedIpsText = string.Join("\n", config.BlacklistedIps);
        }
        
        public RateLimitConfiguration ToConfiguration()
        {
            return new RateLimitConfiguration
            {
                Enabled = Enabled,
                WebhookPolicy = new RateLimitPolicy
                {
                    PolicyId = "webhook",
                    RequestsPerWindow = WebhookRequestsPerMinute,
                    WindowDuration = System.TimeSpan.FromMinutes(1),
                    BurstSize = WebhookBurstSize
                },
                ApiPolicy = new RateLimitPolicy
                {
                    PolicyId = "api",
                    RequestsPerWindow = ApiRequestsPerMinute,
                    WindowDuration = System.TimeSpan.FromMinutes(1),
                    BurstSize = ApiBurstSize
                },
                AdminPolicy = new RateLimitPolicy
                {
                    PolicyId = "admin",
                    RequestsPerWindow = AdminRequestsPerMinute,
                    WindowDuration = System.TimeSpan.FromMinutes(1),
                    BurstSize = AdminBurstSize
                },
                StorePolicy = new RateLimitPolicy
                {
                    PolicyId = "store",
                    RequestsPerWindow = StoreRequestsPerMinute,
                    WindowDuration = System.TimeSpan.FromMinutes(1),
                    BurstSize = StoreBurstSize
                },
                WhitelistedIps = ParseIpList(WhitelistedIpsText),
                BlacklistedIps = ParseIpList(BlacklistedIpsText)
            };
        }
        
        private static List<string> ParseIpList(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();
            
            return text.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(ip => ip.Trim())
                .Where(ip => !string.IsNullOrWhiteSpace(ip))
                .ToList();
        }
    }
}
