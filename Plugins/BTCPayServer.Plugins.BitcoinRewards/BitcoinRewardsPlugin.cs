using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;

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
        
        // Register services
        services.AddScoped<Services.BitcoinRewardsRepository>();
        services.AddScoped<Services.ICashuService, Services.CashuServiceAdapter>();
        services.AddScoped<Services.IEmailNotificationService, Services.EmailNotificationService>();
        services.AddScoped<Services.BitcoinRewardsService>();
        services.AddHttpClient<Clients.SquareApiClient>();
    }
}
