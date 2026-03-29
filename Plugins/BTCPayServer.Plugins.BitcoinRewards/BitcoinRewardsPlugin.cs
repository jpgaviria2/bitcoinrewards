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
    public override string Description => "Bitcoin rewards system for Square POS. Automatically sends Lightning rewards to customers via email or LNURL. Features: real-time analytics, error tracking, rate limiting, webhooks, health monitoring, and advanced reporting.";
    
    public const string PluginNavKey = nameof(BitcoinRewardsPlugin) + "Nav";
    
    // Rate limiting: max 100 concurrent webhook requests
    public static readonly SemaphoreSlim WebhookProcessingLock = new(100, 100);

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=2.3.0" }
    };


    public override void Execute(IServiceCollection services)
    {
        // Phase 2.7: Use BTCPay's existing memory cache (don't override)
        // BTCPay Server already has MemoryCache configured
        
        // Other services
        services.TryAddScoped<Services.BitcoinRewardsRepository>();
        services.TryAddScoped<Services.DatabaseCleanupService>();
        services.TryAddScoped<Services.IEmailNotificationService, Services.EmailNotificationService>();
        services.TryAddScoped<Services.BitcoinRewardsService>();
        services.TryAddScoped<Services.RewardPullPaymentService>();
        services.TryAddScoped<Services.PayoutProcessorDiscoveryService>();
        services.TryAddScoped<Services.PullPaymentStatusService>();
        services.TryAddScoped<Services.BoltCardRewardService>();
        services.TryAddScoped<Services.ExchangeRateService>();
        services.TryAddScoped<Services.CustomerWalletService>();
        services.AddHttpClient<Clients.SquareApiClient>();

        // Production hardening services (v2.0)
        services.AddSingleton<Services.IdempotencyService>();
        
        // Phase 2: Production Hardening
        
        // Health checks
        services.AddHealthChecks()
            .AddCheck<HealthChecks.BitcoinRewardsHealthCheck>(
                "bitcoin-rewards",
                tags: new[] { "bitcoin-rewards", "plugin" });
        
        // Error tracking
        services.TryAddScoped<Services.ErrorTrackingService>();
        
        // Metrics and telemetry
        services.AddSingleton<Services.RewardMetrics>();
        
        // Rate limiting
        services.AddSingleton<Services.RateLimitService>();
        
        // Advanced logging (Phase 2.6)
        services.AddSingleton<Logging.BitcoinRewardsLogEnricher>();
        
        // Performance optimization (Phase 2.7)
        services.AddSingleton<Services.CachingService>();
        
        // Phase 5: Feature Parity
        services.TryAddScoped<Services.AnalyticsService>();
        services.AddHttpClient<Services.WebhookOutService>();
        services.TryAddScoped<Services.WebhookOutService>();
        
        // NIP-05 identity services
        services.AddSingleton<Services.OffensiveWordFilter>();
        services.TryAddScoped<Services.Nip05Service>();
        
        // Lightning Address resolver — hooks into BTCPay's /.well-known/lnurlp/{username}
        services.AddSingleton<Services.LightningAddressResolverFilter>();
        services.AddSingleton<Abstractions.Contracts.IPluginHookFilter>(sp =>
            sp.GetRequiredService<Services.LightningAddressResolverFilter>());
        services.AddHttpContextAccessor();
        
        // BTCPay invoice listener
        services.AddSingleton<HostedServices.BtcpayInvoiceRewardHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<HostedServices.BtcpayInvoiceRewardHostedService>());

        // LNURL claim payment watcher (polls for late Lightning payments)
        services.AddSingleton<HostedServices.LnurlClaimWatcherService>();
        services.AddHostedService(sp => sp.GetRequiredService<HostedServices.LnurlClaimWatcherService>());

        // Maintenance service (cleanup expired cache entries)
        services.AddHostedService<HostedServices.MaintenanceService>();
        
        // Auto-recovery watchdog (Phase 2.5)
        services.AddSingleton<Services.AutoRecoveryWatchdog>();
        services.AddHostedService(sp => sp.GetRequiredService<Services.AutoRecoveryWatchdog>());
        
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
        // Phase 2.6: Correlation ID middleware (must be early in pipeline)
        applicationBuilder.UseMiddleware<Middleware.CorrelationIdMiddleware>();
        
        // Phase 2.4: Rate limiting middleware
        applicationBuilder.UseMiddleware<Middleware.RateLimitingMiddleware>();
        
        base.Execute(applicationBuilder, serviceProvider);
    }
}

