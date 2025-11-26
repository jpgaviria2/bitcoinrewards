using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.BitcoinRewards.CashuPayouts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BTCPayServer.Plugins.BitcoinRewards;

public class BitcoinRewardsPlugin : BaseBTCPayServerPlugin
{
    public override string Identifier => "BTCPayServer.Plugins.BitcoinRewards";
    public override string Name => "Bitcoin Rewards";
    public override string Description => "Bitcoin-backed rewards system that integrates with Shopify to automatically send rewards to customers.";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=2.0.0" }
    };

    public override void Execute(IServiceCollection services)
    {
        // Register navigation menu item - view is in Views/Shared/BitcoinRewardsNavExtension.cshtml
        // Using just the view name (no path) - BTCPay Server automatically searches Views/Shared/
        services.AddUIExtension("header-nav", "BitcoinRewardsNavExtension");
        
        // Register services - using TryAdd to avoid conflicts if already registered
        // Database operations will be handled lazily when needed
        services.TryAddScoped<Services.BitcoinRewardsRepository>();
        services.TryAddScoped<Services.ICashuService, Services.CashuServiceAdapter>();
        services.TryAddScoped<Services.IEmailNotificationService, Services.EmailNotificationService>();
        services.TryAddScoped<Services.BitcoinRewardsService>();
        services.TryAddScoped<Services.PayoutProcessorDiscoveryService>();
        services.AddHttpClient<Clients.SquareApiClient>();

        // Register Cashu payout processor components
        // Using TryAdd to avoid conflicts if BTCNutServer already registered them
        services.TryAddSingleton(provider =>
            (IPayoutHandler)ActivatorUtilities.CreateInstance(provider, typeof(CashuPayoutHandler)));
        
        services.TryAddSingleton<CashuAutomatedPayoutSenderFactory>();
        services.TryAddSingleton<BTCPayServer.PayoutProcessors.IPayoutProcessorFactory>(provider => 
            provider.GetRequiredService<CashuAutomatedPayoutSenderFactory>());
    }
}
