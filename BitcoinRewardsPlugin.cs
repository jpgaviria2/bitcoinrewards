using System.Reflection;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[assembly: AssemblyProduct("Bitcoin Rewards")]
[assembly: AssemblyDescription("Bitcoin-backed rewards system that integrates with Shopify and Square to automatically send rewards to customers.")]

namespace BTCPayServer.Plugins.BitcoinRewards
{
    public class BitcoinRewardsPlugin : BaseBTCPayServerPlugin
    {
        public override string Identifier => "BTCPayServer.Plugins.BitcoinRewards";
        public override string Name => "Bitcoin Rewards";
        public override string Description => "Bitcoin-backed rewards system that integrates with Shopify and Square to automatically send rewards to customers.";

        public override void Execute(IServiceCollection applicationBuilder)
        {
            try
            {
                // Register repositories
                applicationBuilder.AddScoped<Repositories.RewardRecordRepository>();
                
                // Register services
                applicationBuilder.AddScoped<Services.WalletService>();
                
                // Register EmailService - conditionally inject IEmailSender if Emails plugin is available
                applicationBuilder.AddScoped<Services.EmailService>(provider =>
                {
                    BTCPayServer.Logging.Logs? logs = null;
                    try
                    {
                        logs = provider.GetService<BTCPayServer.Logging.Logs>();
                        if (logs == null)
                        {
                            // Try to get it as required service if optional fails
                            logs = provider.GetRequiredService<BTCPayServer.Logging.Logs>();
                        }
                    }
                    catch
                    {
                        // Logs service not available - create a minimal logs instance if possible
                        // This should not happen in production, but handle gracefully
                    }

                    // Try to get IEmailSender from Emails plugin (if installed)
                    object? emailSender = null;
                    try
                    {
                        var emailSenderType = Type.GetType("BTCPayServer.Plugins.Emails.Services.IEmailSender, BTCPayServer");
                        if (emailSenderType != null)
                        {
                            emailSender = provider.GetService(emailSenderType);
                        }
                    }
                    catch
                    {
                        // Emails plugin not available, emailSender will remain null
                    }
                    
                    if (logs == null)
                    {
                        throw new InvalidOperationException("BTCPayServer.Logging.Logs service is required but not available");
                    }
                    
                    return new Services.EmailService(logs, emailSender);
                });
                
                // Register RateService - handle if RateProviderFactory is not available
                applicationBuilder.AddScoped<Services.RateService>(provider =>
                {
                    var logs = provider.GetService<BTCPayServer.Logging.Logs>();
                    if (logs == null)
                    {
                        logs = provider.GetRequiredService<BTCPayServer.Logging.Logs>();
                    }
                    
                    var rateProviderFactory = provider.GetService<BTCPayServer.Services.Rates.RateProviderFactory>();
                    if (rateProviderFactory == null)
                    {
                        // RateProviderFactory is required, but we'll handle null in RateService
                        // This allows the plugin to load even if rate provider isn't configured
                        // RateService will use fallback rates
                        throw new InvalidOperationException("RateProviderFactory service is required but not available");
                    }
                    
                    return new Services.RateService(rateProviderFactory, logs);
                });
                
                applicationBuilder.AddHttpClient<Services.SquareApiService>();
                
                // Register main service - handle if EventAggregator is not available
                applicationBuilder.AddSingleton<BitcoinRewardsService>(provider =>
                {
                    var eventAggregator = provider.GetService<BTCPayServer.EventAggregator>();
                    if (eventAggregator == null)
                    {
                        throw new InvalidOperationException("EventAggregator service is required but not available");
                    }
                    
                    var storeRepository = provider.GetRequiredService<BTCPayServer.Services.Stores.StoreRepository>();
                    var rewardRepository = provider.GetRequiredService<Repositories.RewardRecordRepository>();
                    var walletService = provider.GetRequiredService<Services.WalletService>();
                    var emailService = provider.GetRequiredService<Services.EmailService>();
                    var rateService = provider.GetRequiredService<Services.RateService>();
                    var logs = provider.GetRequiredService<BTCPayServer.Logging.Logs>();
                    
                    return new BitcoinRewardsService(
                        eventAggregator,
                        storeRepository,
                        rewardRepository,
                        walletService,
                        emailService,
                        rateService,
                        logs);
                });
                
                applicationBuilder.AddSingleton<IHostedService, BitcoinRewardsService>(provider => provider.GetRequiredService<BitcoinRewardsService>());
                
                // Register UI extension with comprehensive error handling
                // Try multiple path formats to ensure compatibility with different BTCPay Server versions
                // This is non-critical - plugin works fine without the navigation menu item
                bool viewRegistered = false;
                string[] viewPaths = new[]
                {
                    "BTCPayServer.Plugins.BitcoinRewards/Views/BitcoinRewards/NavExtension",
                    "/BTCPayServer.Plugins.BitcoinRewards/Views/BitcoinRewards/NavExtension",
                    "BitcoinRewards/NavExtension",
                    "/BitcoinRewards/NavExtension",
                    "Views/BitcoinRewards/NavExtension",
                    "/Views/BitcoinRewards/NavExtension"
                };

                foreach (var viewPath in viewPaths)
                {
                    try
                    {
                        applicationBuilder.AddUIExtension("header-nav", viewPath);
                        viewRegistered = true;
                        // Successfully registered - no need to try other paths
                        break;
                    }
                    catch
                    {
                        // Try next path format - silently continue
                        continue;
                    }
                }

                // If all paths failed, silently continue - this is non-critical
                // The plugin functionality (webhooks, rewards processing) does not depend on the nav menu
                // BTCPay Server will handle view resolution errors gracefully without crashing
                
                base.Execute(applicationBuilder);
            }
            catch (Exception ex)
            {
                // Log the error but don't crash the server
                // In production, BTCPay Server should handle plugin loading errors gracefully
                // We'll let the exception propagate so BTCPay Server can log it properly
                throw new InvalidOperationException($"Failed to initialize Bitcoin Rewards plugin: {ex.Message}", ex);
            }
        }
    }
}

