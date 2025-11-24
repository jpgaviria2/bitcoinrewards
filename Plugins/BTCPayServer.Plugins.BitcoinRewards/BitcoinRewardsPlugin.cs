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
        // Temporarily disabled to prevent crashes
        // The view file exists but Views.dll isn't being properly generated/included in the plugin package
        // Once we have a stable minimal plugin, we'll add UI extensions incrementally
        // services.AddUIExtension("header-nav", "BitcoinRewards/NavExtension");
        
        // Plugin loads successfully without UI extensions
    }
}
