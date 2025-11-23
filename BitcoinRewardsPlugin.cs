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
            // Register repositories - always safe
            try
            {
                applicationBuilder.AddScoped<Repositories.RewardRecordRepository>();
                // Logging will be available after service provider is built
            }
            catch
            {
                // Repository registration should never fail, but handle gracefully
                // Continue with other registrations
            }
            
            // Register WalletService - always safe
            try
            {
                applicationBuilder.AddScoped<Services.WalletService>();
                // Logging will be available after service provider is built
            }
            catch
            {
                // WalletService registration should never fail, but handle gracefully
                // Continue with other registrations
            }
            
            // Register EmailService - conditionally inject IEmailSender if Emails plugin is available
            try
            {
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
                        // Logs service not available - this should not happen in production
                        // But we'll handle it gracefully by returning null (will be checked in service)
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
                        // Return null - will be handled as optional service
                        return null!;
                    }
                    
                    return new Services.EmailService(logs, emailSender);
                });
            }
            catch
            {
                // EmailService registration failed - log but continue
                // Email functionality will be unavailable but plugin can still work
            }
            
            // Register RateService - handle if RateProviderFactory is not available
            try
            {
                applicationBuilder.AddScoped<Services.RateService>(provider =>
                {
                    BTCPayServer.Logging.Logs? logs = null;
                    try
                    {
                        logs = provider.GetService<BTCPayServer.Logging.Logs>();
                        if (logs == null)
                        {
                            logs = provider.GetRequiredService<BTCPayServer.Logging.Logs>();
                        }
                    }
                    catch
                    {
                        // Logs not available - return null
                        return null!;
                    }
                    
                    BTCPayServer.Services.Rates.RateProviderFactory? rateProviderFactory = null;
                    try
                    {
                        rateProviderFactory = provider.GetService<BTCPayServer.Services.Rates.RateProviderFactory>();
                    }
                    catch
                    {
                        // RateProviderFactory not available - return null
                        return null!;
                    }
                    
                    if (rateProviderFactory == null || logs == null)
                    {
                        // Return null - will be handled as optional service
                        return null!;
                    }
                    
                    return new Services.RateService(rateProviderFactory, logs);
                });
            }
            catch
            {
                // RateService registration failed - log but continue
                // Rate conversion will be unavailable but plugin can still work
            }
            
            // Register HttpClient for SquareApiService - always safe
            try
            {
                applicationBuilder.AddHttpClient<Services.SquareApiService>();
            }
            catch
            {
                // HttpClient registration should never fail, but handle gracefully
            }
            
            // Register HttpClient for ShopifyApiService - always safe (will be created later)
            try
            {
                applicationBuilder.AddHttpClient<Services.ShopifyApiService>();
            }
            catch
            {
                // HttpClient registration should never fail, but handle gracefully
            }
            
            // Register SquareApiService - always safe (service checks credentials before use)
            try
            {
                applicationBuilder.AddScoped<Services.SquareApiService>(provider =>
                {
                    try
                    {
                        var httpClientFactory = provider.GetRequiredService<System.Net.Http.IHttpClientFactory>();
                        var httpClient = httpClientFactory.CreateClient(nameof(Services.SquareApiService));
                        var logs = provider.GetService<BTCPayServer.Logging.Logs>();
                        if (logs == null)
                        {
                            logs = provider.GetRequiredService<BTCPayServer.Logging.Logs>();
                        }
                        return new Services.SquareApiService(httpClient, logs);
                    }
                    catch
                    {
                        // Return null if service creation fails - will be handled as optional
                        return null!;
                    }
                });
            }
            catch
            {
                // SquareApiService registration failed - log but continue
                // Square integration will be unavailable but plugin can still work
            }
            
            // Register ShopifyApiService - always safe (service checks credentials before use)
            try
            {
                applicationBuilder.AddScoped<Services.ShopifyApiService>(provider =>
                {
                    try
                    {
                        var httpClientFactory = provider.GetRequiredService<System.Net.Http.IHttpClientFactory>();
                        var httpClient = httpClientFactory.CreateClient(nameof(Services.ShopifyApiService));
                        var logs = provider.GetService<BTCPayServer.Logging.Logs>();
                        if (logs == null)
                        {
                            logs = provider.GetRequiredService<BTCPayServer.Logging.Logs>();
                        }
                        return new Services.ShopifyApiService(httpClient, logs);
                    }
                    catch
                    {
                        // Return null if service creation fails - will be handled as optional
                        return null!;
                    }
                });
            }
            catch
            {
                // ShopifyApiService registration failed - log but continue
                // Shopify integration will be unavailable but plugin can still work
            }
            
            // Register main service - handle if optional services are unavailable
            try
            {
                applicationBuilder.AddSingleton<BitcoinRewardsService>(provider =>
                {
                    BTCPayServer.EventAggregator? eventAggregator = null;
                    try
                    {
                        eventAggregator = provider.GetService<BTCPayServer.EventAggregator>();
                        if (eventAggregator == null)
                        {
                            // EventAggregator is required - throw to prevent service creation
                            throw new InvalidOperationException("EventAggregator service is required but not available");
                        }
                    }
                    catch
                    {
                        throw new InvalidOperationException("EventAggregator service is required but not available");
                    }
                    
                    // Required services
                    var storeRepository = provider.GetRequiredService<BTCPayServer.Services.Stores.StoreRepository>();
                    var rewardRepository = provider.GetRequiredService<Repositories.RewardRecordRepository>();
                    var walletService = provider.GetRequiredService<Services.WalletService>();
                    var logs = provider.GetRequiredService<BTCPayServer.Logging.Logs>();
                    
                    // Optional services - can be null
                    var emailService = provider.GetService<Services.EmailService>();
                    var rateService = provider.GetService<Services.RateService>();
                    var squareApiService = provider.GetService<Services.SquareApiService>();
                    var shopifyApiService = provider.GetService<Services.ShopifyApiService>();
                    
                    return new BitcoinRewardsService(
                        eventAggregator,
                        storeRepository,
                        rewardRepository,
                        walletService,
                        emailService,
                        rateService,
                        squareApiService,
                        shopifyApiService,
                        logs);
                });
            }
            catch (Exception ex)
            {
                // BitcoinRewardsService registration failed - this is critical
                // Re-throw to let BTCPay Server handle it
                throw new InvalidOperationException($"Failed to register BitcoinRewardsService: {ex.Message}", ex);
            }
            
            // Register as hosted service
            try
            {
                applicationBuilder.AddSingleton<IHostedService, BitcoinRewardsService>(provider => provider.GetRequiredService<BitcoinRewardsService>());
            }
            catch (Exception ex)
            {
                // Hosted service registration failed - this is critical
                // Re-throw to let BTCPay Server handle it
                throw new InvalidOperationException($"Failed to register BitcoinRewardsService as hosted service: {ex.Message}", ex);
            }
            
            // Register UI extension with error handling
            // Use simple relative path - Razor SDK will handle view resolution from compiled views
            // This is non-critical - plugin works fine without the navigation menu item
            try
            {
                applicationBuilder.AddUIExtension("header-nav", "BitcoinRewards/NavExtension");
            }
            catch
            {
                // View registration failed, but this is not critical for plugin functionality
                // The plugin will work fine without the navigation menu item
                // Users can still access the plugin via direct URL or store settings
                // BTCPay Server will handle view resolution errors gracefully without crashing
            }
            
            base.Execute(applicationBuilder);
        }
    }
}

