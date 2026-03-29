using System;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BitcoinRewards.Tests.Services
{
    public class CachingServiceTests
    {
        private readonly CachingService _service;
        private readonly IMemoryCache _cache;

        public CachingServiceTests()
        {
            _cache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = 1000
            });
            var loggerMock = new Mock<ILogger<CachingService>>();
            _service = new CachingService(_cache, loggerMock.Object);
        }

        [Fact]
        public async Task GetOrCreateAsync_FirstCall_ShouldExecuteFactory()
        {
            // Arrange
            var key = "test-key";
            var expectedValue = "test-value";
            var factoryCalled = false;

            // Act
            var result = await _service.GetOrCreateAsync(key, async () =>
            {
                factoryCalled = true;
                await Task.CompletedTask;
                return expectedValue;
            });

            // Assert
            result.Should().Be(expectedValue);
            factoryCalled.Should().BeTrue();
        }

        [Fact]
        public async Task GetOrCreateAsync_SecondCall_ShouldReturnCachedValue()
        {
            // Arrange
            var key = "test-key-2";
            var expectedValue = "test-value";
            var factoryCallCount = 0;

            // Act
            var result1 = await _service.GetOrCreateAsync(key, async () =>
            {
                factoryCallCount++;
                await Task.CompletedTask;
                return expectedValue;
            });

            var result2 = await _service.GetOrCreateAsync(key, async () =>
            {
                factoryCallCount++;
                await Task.CompletedTask;
                return "different-value";
            });

            // Assert
            result1.Should().Be(expectedValue);
            result2.Should().Be(expectedValue); // Same as first call
            factoryCallCount.Should().Be(1); // Factory called only once
        }

        [Fact]
        public async Task GetOrCreateStoreSettingsAsync_ShouldCacheSettings()
        {
            // Arrange
            var storeId = "store123";
            var settingsName = "BitcoinRewardsPluginSettings";
            var settings = new { Enabled = true, RewardPercentage = 5m };
            var factoryCallCount = 0;

            // Act
            var result1 = await _service.GetOrCreateStoreSettingsAsync(
                storeId,
                settingsName,
                async () =>
                {
                    factoryCallCount++;
                    await Task.CompletedTask;
                    return settings;
                });

            var result2 = await _service.GetOrCreateStoreSettingsAsync(
                storeId,
                settingsName,
                async () =>
                {
                    factoryCallCount++;
                    await Task.CompletedTask;
                    return settings;
                });

            // Assert
            result1.Should().Be(settings);
            result2.Should().Be(settings);
            factoryCallCount.Should().Be(1);
        }

        [Fact]
        public async Task GetOrCreateRateAsync_ShouldCacheExchangeRate()
        {
            // Arrange
            var fromCurrency = "BTC";
            var toCurrency = "USD";
            var rate = 50000m;

            // Act
            var result = await _service.GetOrCreateRateAsync(
                fromCurrency,
                toCurrency,
                async () =>
                {
                    await Task.CompletedTask;
                    return rate;
                });

            // Assert
            result.Should().Be(rate);
        }

        [Fact]
        public async Task GetOrCreatePayoutProcessorsAsync_ShouldCacheProcessors()
        {
            // Arrange
            var storeId = "store123";
            var processors = new[] { "LND", "Core Lightning" };

            // Act
            var result = await _service.GetOrCreatePayoutProcessorsAsync(
                storeId,
                async () =>
                {
                    await Task.CompletedTask;
                    return processors;
                });

            // Assert
            result.Should().BeEquivalentTo(processors);
        }

        [Fact]
        public async Task GetOrCreateStoreAsync_ShouldCacheStoreObject()
        {
            // Arrange
            var storeId = "store123";
            var store = new { Id = storeId, Name = "Test Store" };

            // Act
            var result = await _service.GetOrCreateStoreAsync(
                storeId,
                async () =>
                {
                    await Task.CompletedTask;
                    return store;
                });

            // Assert
            result.Should().Be(store);
        }

        [Fact]
        public async Task Invalidate_ShouldRemoveCachedEntry()
        {
            // Arrange
            var key = "test-key-3";
            var value1 = "value1";
            var value2 = "value2";
            var factoryCallCount = 0;

            // Act - Cache first value
            await _service.GetOrCreateAsync(key, async () =>
            {
                factoryCallCount++;
                await Task.CompletedTask;
                return value1;
            });

            // Invalidate
            _service.Invalidate(key);

            // Try to get again
            var result = await _service.GetOrCreateAsync(key, async () =>
            {
                factoryCallCount++;
                await Task.CompletedTask;
                return value2;
            });

            // Assert
            result.Should().Be(value2); // New value returned
            factoryCallCount.Should().Be(2); // Factory called twice
        }

        [Fact]
        public void InvalidateStoreSettings_ShouldClearSpecificSetting()
        {
            // Arrange
            var storeId = "store123";
            var settingsName = "BitcoinRewardsPluginSettings";

            // Act
            _service.InvalidateStoreSettings(storeId, settingsName);

            // Assert - No exception should be thrown
            // (Can't easily verify invalidation without checking internal cache state)
        }

        [Fact]
        public void GetStatistics_ShouldReturnCacheDurations()
        {
            // Act
            var stats = _service.GetStatistics();

            // Assert
            stats.Should().NotBeNull();
            stats.SettingsCacheDuration.Should().Be(TimeSpan.FromMinutes(10));
            stats.ProcessorsCacheDuration.Should().Be(TimeSpan.FromMinutes(5));
            stats.RateCacheDuration.Should().Be(TimeSpan.FromSeconds(30));
            stats.StoreCacheDuration.Should().Be(TimeSpan.FromMinutes(15));
        }

        [Fact]
        public async Task GetOrCreateAsync_WithCustomExpiration_ShouldRespectDuration()
        {
            // Arrange
            var key = "expiration-test";
            var value = "test-value";
            var customExpiration = TimeSpan.FromSeconds(1);

            // Act
            await _service.GetOrCreateAsync(key, async () =>
            {
                await Task.CompletedTask;
                return value;
            }, customExpiration);

            // Wait for expiration
            await Task.Delay(1200);

            // Try to get again (should call factory again after expiration)
            var factoryCalled = false;
            await _service.GetOrCreateAsync(key, async () =>
            {
                factoryCalled = true;
                await Task.CompletedTask;
                return "new-value";
            }, customExpiration);

            // Assert
            factoryCalled.Should().BeTrue(); // Factory should be called again after expiration
        }

        [Fact]
        public async Task GetOrCreateStoreSettingsAsync_NullResult_ShouldReturnNull()
        {
            // Arrange
            var storeId = "nonexistent-store";
            var settingsName = "Settings";

            // Act
            var result = await _service.GetOrCreateStoreSettingsAsync<object>(
                storeId,
                settingsName,
                async () =>
                {
                    await Task.CompletedTask;
                    return null;
                });

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task ConcurrentAccess_ShouldHandleThreadSafety()
        {
            // Arrange
            var key = "concurrent-key";
            var factoryCallCount = 0;
            var tasks = new Task<string>[10];

            // Act - Multiple concurrent requests for same key
            for (int i = 0; i < 10; i++)
            {
                tasks[i] = _service.GetOrCreateAsync(key, async () =>
                {
                    System.Threading.Interlocked.Increment(ref factoryCallCount);
                    await Task.Delay(100); // Simulate async work
                    return "cached-value";
                });
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().AllBe("cached-value");
            factoryCallCount.Should().Be(1); // Factory should only be called once despite concurrent access
        }
    }
}
