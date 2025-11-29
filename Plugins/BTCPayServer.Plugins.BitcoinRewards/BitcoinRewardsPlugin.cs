using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Reflection;
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

    // Module initializer runs before any type loading, ensuring assembly resolver is registered early
    [ModuleInitializer]
    internal static void InitializeAssemblyResolver()
    {
        // Register assembly resolver to gracefully handle missing optional dependencies
        // This prevents ReflectionTypeLoadException when Cashu plugin is not installed
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var assemblyName = new AssemblyName(args.Name);
            
            // If trying to load Cashu plugin assembly, return null (optional dependency)
            if (assemblyName.Name == "BTCPayServer.Plugins.Cashu")
            {
                return null; // Let it fail gracefully - we handle this via reflection
            }
            
            // Try to resolve DotNut from the plugin directory
            if (assemblyName.Name == "DotNut")
            {
                // Get the plugin directory (where this assembly is located)
                var pluginAssembly = typeof(BitcoinRewardsPlugin).Assembly;
                var pluginLocation = Path.GetDirectoryName(pluginAssembly.Location);
                if (pluginLocation != null)
                {
                    var dotNutPath = Path.Combine(pluginLocation, "DotNut.dll");
                    if (File.Exists(dotNutPath))
                    {
                        return Assembly.LoadFrom(dotNutPath);
                    }
                }
            }
            
            // For other assemblies, let the default resolver handle it
            return null;
        };
    }

    public override void Execute(IServiceCollection services)
    {
        // UI extensions
        services.AddUIExtension("header-nav", "BitcoinRewardsNavExtension");
        services.AddUIExtension("store-wallets-nav", "WalletNavExtension");

        // Database Services - matches Cashu plugin pattern exactly
        services.AddSingleton<Data.BitcoinRewardsPluginDbContextFactory>();
        services.AddDbContext<Data.BitcoinRewardsPluginDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<Data.BitcoinRewardsPluginDbContextFactory>();
            factory.ConfigureBuilder(o);
        });
        services.AddHostedService<Data.BitcoinRewardsMigrationRunner>();
        
        // Wallet Services
        services.AddSingleton<WalletStatusProvider>();
        
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

        // Payout Processor Registration
        services.AddSingleton<CashuAutomatedPayoutSenderFactory>();
        services.AddSingleton<BTCPayServer.PayoutProcessors.IPayoutProcessorFactory>(provider => 
            provider.GetRequiredService<CashuAutomatedPayoutSenderFactory>());
        
        services.TryAddSingleton(provider =>
            (IPayoutHandler)ActivatorUtilities.CreateInstance(provider, typeof(CashuPayoutHandler)));
            
        base.Execute(services);
    }
}
