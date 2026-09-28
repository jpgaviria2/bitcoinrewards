using System;
using System.Threading;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BTCPayServer.Plugins.BitcoinRewards;

public class BitcoinRewardsPlugin : BaseBTCPayServerPlugin
{
    public override string Identifier => "BTCPayServer.Plugins.BitcoinRewards";
    public override string Name => "Bitcoin Rewards";
    public override string Description => "Square POS Bitcoin rewards for BTCPay Server. Processes verified Square payment webhooks and creates BTCPay pull-payment rewards.";
    
    public const string PluginNavKey = nameof(BitcoinRewardsPlugin) + "Nav";
    
    // Rate limiting: max 100 concurrent webhook requests
    public static readonly SemaphoreSlim WebhookProcessingLock = new(100, 100);

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=2.3.0" }
    };


    public override void Execute(IServiceCollection services)
    {
        // Square-only core services. Keep the runtime small and avoid registering
        // legacy/experimental features (customer wallets, LNURL/NIP-05,
        // error dashboards, analytics, auto-recovery) that are not needed for the
        // Square webhook -> BTCPay pull-payment reward path.
        services.TryAddScoped<Services.BitcoinRewardsRepository>();
        services.TryAddScoped<Services.IEmailNotificationService, Services.EmailNotificationService>();
        services.TryAddScoped<Services.BitcoinRewardsService>();
        services.TryAddScoped<Services.RewardPullPaymentService>();
        services.TryAddScoped<Services.PayoutProcessorDiscoveryService>();
        services.TryAddScoped<Services.PullPaymentStatusService>();
        services.TryAddScoped<Services.ExchangeRateService>();
        services.TryAddScoped<Services.CustomerOrderAssociationService>();
        services.TryAddScoped<Services.PendingLightningAddressCheckInService>();
        services.TryAddScoped<Services.DirectLightningPayoutService>();
        services.AddHttpClient<Services.CustomerProfileClient>();
        services.AddHttpClient<Clients.SquareApiClient>();

        // Lightweight idempotency/metrics/rate limiting used by the Square path.
        services.AddSingleton<Services.IdempotencyService>();
        services.AddSingleton<Services.RewardMetrics>();
        services.AddSingleton<Services.RateLimitService>();
        services.AddSingleton<Services.CachingService>();

        services.AddHttpContextAccessor();
        
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
        services.AddHostedService<HostedServices.LegacyPullPaymentDirectSettlementService>();
            
        base.Execute(services);
    }
    
    public override void Execute(Microsoft.AspNetCore.Builder.IApplicationBuilder applicationBuilder,
        IServiceProvider serviceProvider)
    {
        // Request rate limiting for Square webhook endpoints.
        applicationBuilder.UseMiddleware<Middleware.RateLimitingMiddleware>();
        
        base.Execute(applicationBuilder, serviceProvider);
    }
}
