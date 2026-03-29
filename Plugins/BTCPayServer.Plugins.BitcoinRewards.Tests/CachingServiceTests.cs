using System;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using FluentAssertions;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests;

/// <summary>
/// Tests for CachingService — validates cache operations work without SizeLimit
/// and that the service uses BTCPay's existing IMemoryCache correctly.
/// </summary>
public class CachingServiceTests
{
    private readonly CachingService _cachingService;
    private readonly IMemoryCache _cache;

    public CachingServiceTests()
    {
        // Use a MemoryCache WITHOUT SizeLimit (matching the production fix)
        _cache = new MemoryCache(new MemoryCacheOptions());
        _cachingService = new CachingService(_cache, NullLogger<CachingService>.Instance);
    }

    [Fact]
    public async Task GetOrCreateAsync_ReturnsCachedValue()
    {
        // Arrange
        var callCount = 0;

        // Act
        var result1 = await _cachingService.GetOrCreateAsync("test-key", () =>
        {
            callCount++;
            return Task.FromResult("hello");
        });

        var result2 = await _cachingService.GetOrCreateAsync("test-key", () =>
        {
            callCount++;
            return Task.FromResult("should not be called");
        });

        // Assert
        result1.Should().Be("hello");
        result2.Should().Be("hello", "second call should return cached value");
        callCount.Should().Be(1, "factory should only be called once");
    }

    [Fact]
    public async Task GetOrCreateAsync_UsesSlidingExpiration()
    {
        // Arrange
        var expiration = TimeSpan.FromMilliseconds(50);

        // Act
        var result1 = await _cachingService.GetOrCreateAsync("expiring-key", () =>
            Task.FromResult("value1"), expiration);

        result1.Should().Be("value1");

        // Wait for expiration
        await Task.Delay(100);

        var result2 = await _cachingService.GetOrCreateAsync("expiring-key", () =>
            Task.FromResult("value2"), expiration);

        // Assert
        result2.Should().Be("value2", "cache entry should have expired");
    }

    [Fact]
    public async Task GetOrCreateStoreSettingsAsync_CachesAndReturnsSettings()
    {
        // Arrange
        var callCount = 0;
        var settings = new TestSettings { Name = "test" };

        // Act
        var result1 = await _cachingService.GetOrCreateStoreSettingsAsync<TestSettings>(
            "store1", "config", () =>
            {
                callCount++;
                return Task.FromResult<TestSettings?>(settings);
            });

        var result2 = await _cachingService.GetOrCreateStoreSettingsAsync<TestSettings>(
            "store1", "config", () =>
            {
                callCount++;
                return Task.FromResult<TestSettings?>(new TestSettings { Name = "different" });
            });

        // Assert
        result1.Should().NotBeNull();
        result1!.Name.Should().Be("test");
        result2!.Name.Should().Be("test", "second call should return cached settings");
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateStoreSettingsAsync_DifferentStores_CacheSeparately()
    {
        // Act
        var result1 = await _cachingService.GetOrCreateStoreSettingsAsync<TestSettings>(
            "store1", "config", () => Task.FromResult<TestSettings?>(new TestSettings { Name = "store1" }));

        var result2 = await _cachingService.GetOrCreateStoreSettingsAsync<TestSettings>(
            "store2", "config", () => Task.FromResult<TestSettings?>(new TestSettings { Name = "store2" }));

        // Assert
        result1!.Name.Should().Be("store1");
        result2!.Name.Should().Be("store2");
    }

    [Fact]
    public async Task GetOrCreateRateAsync_CachesAndReturnsRate()
    {
        // Arrange
        var callCount = 0;

        // Act
        var result1 = await _cachingService.GetOrCreateRateAsync<decimal>(
            "USD", "BTC", () =>
            {
                callCount++;
                return Task.FromResult<decimal?>(50000m);
            });

        var result2 = await _cachingService.GetOrCreateRateAsync<decimal>(
            "USD", "BTC", () =>
            {
                callCount++;
                return Task.FromResult<decimal?>(99999m);
            });

        // Assert
        result1.Should().Be(50000m);
        result2.Should().Be(50000m, "second call should return cached rate");
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateStoreAsync_CachesAndReturnsStore()
    {
        // Arrange
        var callCount = 0;

        // Act
        var result1 = await _cachingService.GetOrCreateStoreAsync<TestSettings>(
            "store1", () =>
            {
                callCount++;
                return Task.FromResult<TestSettings?>(new TestSettings { Name = "cached-store" });
            });

        var result2 = await _cachingService.GetOrCreateStoreAsync<TestSettings>(
            "store1", () =>
            {
                callCount++;
                return Task.FromResult<TestSettings?>(new TestSettings { Name = "different" });
            });

        // Assert
        result1!.Name.Should().Be("cached-store");
        result2!.Name.Should().Be("cached-store", "second call should return cached store");
        callCount.Should().Be(1);
    }

    [Fact]
    public void Invalidate_RemovesCacheEntry()
    {
        // Arrange
        _cache.Set("test-invalidate", "value");

        // Act
        _cachingService.Invalidate("test-invalidate");

        // Assert
        _cache.TryGetValue("test-invalidate", out _).Should().BeFalse("entry should be removed");
    }

    [Fact]
    public void InvalidateStoreSettings_RemovesSpecificSettingEntry()
    {
        // Arrange
        var key = "btcrewards:settings:store1:config";
        _cache.Set(key, "value");

        // Act
        _cachingService.InvalidateStoreSettings("store1", "config");

        // Assert
        _cache.TryGetValue(key, out _).Should().BeFalse("specific settings entry should be removed");
    }

    [Fact]
    public void GetStatistics_ReturnsNonNullStatistics()
    {
        // Act
        var stats = _cachingService.GetStatistics();

        // Assert
        stats.Should().NotBeNull();
        stats.SettingsCacheDuration.Should().BeGreaterThan(TimeSpan.Zero);
        stats.ProcessorsCacheDuration.Should().BeGreaterThan(TimeSpan.Zero);
        stats.RateCacheDuration.Should().BeGreaterThan(TimeSpan.Zero);
        stats.StoreCacheDuration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task CacheEntries_WorkWithoutSizeLimit()
    {
        // This test verifies the production fix: cache entries with Size=1 work
        // when the underlying MemoryCache has NO SizeLimit set.
        // Previously, calling AddMemoryCache with SizeLimit crashed BTCPay.

        var cache = new MemoryCache(new MemoryCacheOptions()); // NO SizeLimit
        var service = new CachingService(cache, NullLogger<CachingService>.Instance);

        // Act & Assert: should not throw even though entries set Size=1
        var act = async () =>
        {
            await service.GetOrCreateAsync("key1", () => Task.FromResult("val1"));
            await service.GetOrCreateStoreSettingsAsync<TestSettings>("s", "c",
                () => Task.FromResult<TestSettings?>(new TestSettings { Name = "test" }));
            await service.GetOrCreateRateAsync<decimal>("USD", "BTC",
                () => Task.FromResult<decimal?>(50000m));
            await service.GetOrCreateStoreAsync<TestSettings>("s",
                () => Task.FromResult<TestSettings?>(new TestSettings { Name = "test" }));
        };

        await act.Should().NotThrowAsync(
            "cache operations must work without SizeLimit on the underlying MemoryCache");
    }

    private class TestSettings
    {
        public string Name { get; set; } = string.Empty;
    }
}
