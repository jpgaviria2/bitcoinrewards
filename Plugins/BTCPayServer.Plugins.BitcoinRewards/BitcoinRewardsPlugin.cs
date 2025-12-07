using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BTCPayServer.Plugins.BitcoinRewards;

public class BitcoinRewardsPlugin : BaseBTCPayServerPlugin
{
    public override string Identifier => "BTCPayServer.Plugins.BitcoinRewards";
    public override string Name => "Bitcoin Rewards";
    public override string Description => "Bitcoin-backed rewards system that integrates with Shopify to automatically send rewards to customers.";
    
    public const string PluginNavKey = nameof(BitcoinRewardsPlugin) + "Nav";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=2.0.0" }
    };


    public override void Execute(IServiceCollection services)
    {
        // Other services
        services.TryAddScoped<Services.BitcoinRewardsRepository>();
        services.TryAddScoped<Services.DatabaseCleanupService>();
        services.TryAddScoped<Services.IEmailNotificationService, Services.EmailNotificationService>();
        services.TryAddScoped<Services.BitcoinRewardsService>();
        services.TryAddScoped<Services.RewardPullPaymentService>();
        services.TryAddScoped<Services.PayoutProcessorDiscoveryService>();
        services.AddHttpClient<Clients.SquareApiClient>();

        // BTCPay invoice listener
        services.AddSingleton<HostedServices.BtcpayInvoiceRewardHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<HostedServices.BtcpayInvoiceRewardHostedService>());
        
        // UI extensions
        services.AddUIExtension("header-nav", "BitcoinRewardsNavExtension");

        // Database Services (matches Cashu plugin pattern exactly)
        services.AddSingleton<Data.BitcoinRewardsPluginDbContextFactory>();
        services.AddDbContext<Data.BitcoinRewardsPluginDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<Data.BitcoinRewardsPluginDbContextFactory>();
            factory.ConfigureBuilder(o);
        });
        services.AddHostedService<Data.BitcoinRewardsMigrationRunner>();
            
        base.Execute(services);
    }
}

