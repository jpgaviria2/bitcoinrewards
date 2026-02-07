using System;
using System.Threading;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BTCPayServer.Plugins.BitcoinRewards;

public class BitcoinRewardsPlugin : BaseBTCPayServerPlugin
{
    public override string Identifier => "BTCPayServer.Plugins.BitcoinRewards";
    public override string Name => "Bitcoin Rewards";
    public override string Description => "Bitcoin-backed rewards system that integrates with Shopify to automatically send rewards to customers.";
    
    public const string PluginNavKey = nameof(BitcoinRewardsPlugin) + "Nav";
    
    // Rate limiting: max 100 concurrent webhook requests
    public static readonly SemaphoreSlim WebhookProcessingLock = new(100, 100);

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=2.3.0" }
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
        services.TryAddScoped<Services.PullPaymentStatusService>();
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
    
    public override void Execute(Microsoft.AspNetCore.Builder.IApplicationBuilder applicationBuilder,
        IServiceProvider serviceProvider)
    {
        base.Execute(applicationBuilder, serviceProvider);
    }
}

