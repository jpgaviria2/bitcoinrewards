using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.BitcoinRewards.CashuPayouts;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.PaymentHandlers;
using BTCPayServer.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BTCPayServer.Plugins.BitcoinRewards;

public class BitcoinRewardsPlugin : BaseBTCPayServerPlugin
{
    public override string Identifier => "BTCPayServer.Plugins.BitcoinRewards";
    public override string Name => "Bitcoin Rewards";
    public override string Description => "Bitcoin-backed rewards system that integrates with Shopify to automatically send rewards to customers.";
    
    public const string PluginNavKey = nameof(BitcoinRewardsPlugin) + "Nav";
    
    internal static PaymentMethodId WalletPmid = new PaymentMethodId("BITCOINREWARDS-WALLET");

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=2.0.0" }
    };


    public override void Execute(IServiceCollection services)
    {
        // UI extensions
        services.AddUIExtension("header-nav", "BitcoinRewardsNavExtension");
        services.AddUIExtension("store-wallets-nav", "WalletNavExtension");

        // Database Services - matches Cashu plugin pattern exactly
        try
        {
            services.AddSingleton<Data.BitcoinRewardsPluginDbContextFactory>();
            services.AddDbContext<Data.BitcoinRewardsPluginDbContext>((provider, o) =>
            {
                var factory = provider.GetRequiredService<Data.BitcoinRewardsPluginDbContextFactory>();
                factory.ConfigureBuilder(o);
            });
            services.AddHostedService<Data.BitcoinRewardsMigrationRunner>();
        }
        catch (Exception ex)
        {
            // If database services can't be registered, log warning but don't crash
            System.Console.WriteLine($"Warning: Could not register database services. Database functionality will be disabled. Error: {ex.Message}");
        }
        
        // Payment Method Handler Registration (wrapped in try-catch to prevent crashes)
        try
        {
            services.AddSingleton(provider => 
                (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider, typeof(PaymentHandlers.WalletPaymentMethodHandler)));
            services.AddDefaultPrettyName(WalletPmid, "Bitcoin Rewards Wallet");
            
            // Wallet Services
            services.AddSingleton<PaymentHandlers.WalletStatusProvider>();
        }
        catch (Exception ex)
        {
            // If payment handler types can't be loaded, log warning but don't crash
            System.Console.WriteLine($"Warning: Could not register Payment Method Handler. Wallet functionality will be disabled. Error: {ex.Message}");
        }
        
        // Other services
        services.TryAddScoped<Services.BitcoinRewardsRepository>();
        services.TryAddScoped<Services.DatabaseCleanupService>();
        services.TryAddScoped<Services.ProofStorageService>();
        services.TryAddScoped<Services.WalletConfigurationService>();
        services.TryAddScoped<Services.ICashuService, Services.CashuServiceAdapter>();
        services.TryAddScoped<Services.IEmailNotificationService, Services.EmailNotificationService>();
        services.TryAddScoped<Services.BitcoinRewardsService>();
        services.TryAddScoped<Services.PayoutProcessorDiscoveryService>();
        services.AddHttpClient<Clients.SquareApiClient>();

        // Payout Processor Registration (using try-catch to prevent crashes if Cashu types can't be loaded)
        try
        {
            services.AddSingleton<CashuPayouts.CashuAutomatedPayoutSenderFactory>();
            services.AddSingleton<BTCPayServer.PayoutProcessors.IPayoutProcessorFactory>(provider => 
                provider.GetRequiredService<CashuPayouts.CashuAutomatedPayoutSenderFactory>());
            
            services.TryAddSingleton(provider =>
                (IPayoutHandler)ActivatorUtilities.CreateInstance(provider, typeof(CashuPayouts.CashuPayoutHandler)));
        }
        catch (Exception ex)
        {
            // If Cashu payout types can't be loaded (e.g., missing DotNut.dll), log warning but don't crash
            // The plugin will still work for wallet functionality
            System.Console.WriteLine($"Warning: Could not register Cashu Payout Processor. Payout functionality will be disabled. Error: {ex.Message}");
        }
            
        base.Execute(services);
    }
}

