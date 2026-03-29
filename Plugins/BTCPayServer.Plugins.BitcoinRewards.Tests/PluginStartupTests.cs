using System;
using System.Linq;
using BTCPayServer.Plugins.BitcoinRewards;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Plugins.BitcoinRewards.Middleware;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.HealthChecks;
using BTCPayServer.Plugins.BitcoinRewards.Logging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FluentAssertions;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests;

/// <summary>
/// CRITICAL: Tests that the plugin startup (Execute) does not break BTCPay Server.
/// This was the root cause of a production crash where AddMemoryCache() with SizeLimit
/// replaced BTCPay's global cache.
/// </summary>
public class PluginStartupTests
{
    private readonly BitcoinRewardsPlugin _plugin;
    private readonly ServiceCollection _services;

    public PluginStartupTests()
    {
        _plugin = new BitcoinRewardsPlugin();
        _services = new ServiceCollection();
    }

    [Fact]
    public void Execute_DoesNotCallAddMemoryCache()
    {
        // Arrange: pre-register a MemoryCache (simulating BTCPay's existing cache)
        var existingCache = new MemoryCache(new MemoryCacheOptions());
        _services.AddSingleton<IMemoryCache>(existingCache);

        // Act
        _plugin.Execute(_services);

        // Assert: The IMemoryCache registration should still resolve to our original instance
        var provider = _services.BuildServiceProvider();
        var resolvedCache = provider.GetRequiredService<IMemoryCache>();
        resolvedCache.Should().BeSameAs(existingCache,
            "plugin must NOT replace BTCPay's existing IMemoryCache (this caused a production crash)");
    }

    [Fact]
    public void Execute_DoesNotRegisterMemoryCacheWithSizeLimit()
    {
        // Arrange: register a default memory cache like BTCPay does
        _services.AddMemoryCache();

        // Act
        _plugin.Execute(_services);

        // Assert: no IMemoryCache descriptor should have a SizeLimit factory
        // The plugin must not add any IMemoryCache registrations
        var memoryCacheDescriptors = _services.Where(d => d.ServiceType == typeof(IMemoryCache)).ToList();

        // There should only be the one we added, not additional ones from the plugin
        memoryCacheDescriptors.Should().HaveCount(1,
            "plugin should not add additional IMemoryCache registrations");
    }

    [Fact]
    public void Execute_DoesNotReplaceGlobalSingletonServices()
    {
        // Arrange: simulate services that BTCPay Server registers globally
        _services.AddMemoryCache();
        _services.AddLogging();

        var memoryCacheCountBefore = _services.Count(d => d.ServiceType == typeof(IMemoryCache));

        // Act
        _plugin.Execute(_services);

        // Assert
        var memoryCacheCountAfter = _services.Count(d => d.ServiceType == typeof(IMemoryCache));
        memoryCacheCountAfter.Should().Be(memoryCacheCountBefore,
            "plugin must not add new IMemoryCache registrations that could override BTCPay's cache");
    }

    [Fact]
    public void Execute_RegistersBitcoinRewardsRepository()
    {
        _services.AddLogging();
        _plugin.Execute(_services);
        AssertServiceRegistered<BitcoinRewardsRepository>(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Execute_RegistersDatabaseCleanupService()
    {
        _services.AddLogging();
        _plugin.Execute(_services);
        AssertServiceRegistered<DatabaseCleanupService>(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Execute_RegistersEmailNotificationService()
    {
        _services.AddLogging();
        _plugin.Execute(_services);

        var descriptor = _services.FirstOrDefault(d => d.ServiceType == typeof(IEmailNotificationService));
        descriptor.Should().NotBeNull("IEmailNotificationService should be registered");
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
        descriptor.ImplementationType.Should().Be(typeof(EmailNotificationService));
    }

    [Fact]
    public void Execute_RegistersBitcoinRewardsService()
    {
        _services.AddLogging();
        _plugin.Execute(_services);
        AssertServiceRegistered<BitcoinRewardsService>(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Execute_RegistersRewardPullPaymentService()
    {
        _services.AddLogging();
        _plugin.Execute(_services);
        AssertServiceRegistered<RewardPullPaymentService>(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Execute_RegistersIdempotencyService()
    {
        _services.AddLogging();
        _plugin.Execute(_services);
        AssertServiceRegistered<IdempotencyService>(ServiceLifetime.Singleton);
    }

    [Fact]
    public void Execute_RegistersErrorTrackingService()
    {
        _services.AddLogging();
        _plugin.Execute(_services);
        AssertServiceRegistered<ErrorTrackingService>(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Execute_RegistersRewardMetrics()
    {
        _services.AddLogging();
        _plugin.Execute(_services);
        AssertServiceRegistered<RewardMetrics>(ServiceLifetime.Singleton);
    }

    [Fact]
    public void Execute_RegistersRateLimitService()
    {
        _services.AddLogging();
        _plugin.Execute(_services);
        AssertServiceRegistered<RateLimitService>(ServiceLifetime.Singleton);
    }

    [Fact]
    public void Execute_RegistersCachingService()
    {
        _services.AddLogging();
        _plugin.Execute(_services);
        AssertServiceRegistered<CachingService>(ServiceLifetime.Singleton);
    }

    [Fact]
    public void Execute_RegistersLogEnricher()
    {
        _services.AddLogging();
        _plugin.Execute(_services);
        AssertServiceRegistered<BitcoinRewardsLogEnricher>(ServiceLifetime.Singleton);
    }

    [Fact]
    public void Execute_RegistersCustomerWalletService()
    {
        _services.AddLogging();
        _plugin.Execute(_services);
        AssertServiceRegistered<CustomerWalletService>(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Execute_RegistersExchangeRateService()
    {
        _services.AddLogging();
        _plugin.Execute(_services);
        AssertServiceRegistered<ExchangeRateService>(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Execute_RegistersBoltCardRewardService()
    {
        _services.AddLogging();
        _plugin.Execute(_services);
        AssertServiceRegistered<BoltCardRewardService>(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Execute_RegistersNip05Service()
    {
        _services.AddLogging();
        _plugin.Execute(_services);
        AssertServiceRegistered<Nip05Service>(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Execute_RegistersOffensiveWordFilter()
    {
        _services.AddLogging();
        _plugin.Execute(_services);
        AssertServiceRegistered<OffensiveWordFilter>(ServiceLifetime.Singleton);
    }

    [Fact]
    public void Execute_RegistersAnalyticsService()
    {
        _services.AddLogging();
        _plugin.Execute(_services);
        AssertServiceRegistered<AnalyticsService>(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Execute_RegistersAutoRecoveryWatchdog()
    {
        _services.AddLogging();
        _plugin.Execute(_services);
        AssertServiceRegistered<AutoRecoveryWatchdog>(ServiceLifetime.Singleton);
    }

    [Fact]
    public void Execute_RegistersDbContextFactory()
    {
        _services.AddLogging();
        _plugin.Execute(_services);
        AssertServiceRegistered<BitcoinRewardsPluginDbContextFactory>(ServiceLifetime.Singleton);
    }

    [Fact]
    public void Execute_UsesTryAddScoped_DoesNotOverrideExistingRegistrations()
    {
        // Arrange: pre-register a service the plugin also registers via TryAddScoped
        _services.AddLogging();
        _services.AddScoped<BitcoinRewardsRepository>();

        var countBefore = _services.Count(d => d.ServiceType == typeof(BitcoinRewardsRepository));

        // Act
        _plugin.Execute(_services);

        // Assert: TryAddScoped should NOT add a duplicate
        var countAfter = _services.Count(d => d.ServiceType == typeof(BitcoinRewardsRepository));
        countAfter.Should().Be(countBefore,
            "TryAddScoped must not add duplicate registrations for already-registered services");
    }

    [Fact]
    public void Execute_DoesNotThrow()
    {
        // Arrange
        _services.AddLogging();
        _services.AddMemoryCache();

        // Act & Assert
        var act = () => _plugin.Execute(_services);
        act.Should().NotThrow("Execute must never throw during plugin startup");
    }

    [Fact]
    public void PluginIdentifier_IsCorrect()
    {
        _plugin.Identifier.Should().Be("BTCPayServer.Plugins.BitcoinRewards");
    }

    [Fact]
    public void PluginName_IsCorrect()
    {
        _plugin.Name.Should().Be("Bitcoin Rewards");
    }

    [Fact]
    public void PluginDependencies_RequiresBTCPayServer()
    {
        _plugin.Dependencies.Should().HaveCount(1);
        _plugin.Dependencies[0].Identifier.Should().Be("BTCPayServer");
        _plugin.Dependencies[0].Condition.Should().Be(">=2.3.0");
    }

    private void AssertServiceRegistered<T>(ServiceLifetime expectedLifetime)
    {
        var descriptor = _services.FirstOrDefault(d =>
            d.ServiceType == typeof(T) || d.ImplementationType == typeof(T));
        descriptor.Should().NotBeNull($"{typeof(T).Name} should be registered");
        descriptor!.Lifetime.Should().Be(expectedLifetime,
            $"{typeof(T).Name} should be registered as {expectedLifetime}");
    }
}
