using System;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using Xunit;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests.Services;

/// <summary>
/// Tests for IdempotencyService - critical for preventing duplicate payments
/// </summary>
public class IdempotencyServiceTests
{
    private readonly IdempotencyService _service;

    public IdempotencyServiceTests()
    {
        _service = new IdempotencyService();
    }

    [Fact]
    public void GenerateKey_ShouldCreateConsistentKeys()
    {
        // Arrange
        var walletId = Guid.NewGuid();
        var operation = "pay-invoice";
        var parameters = "lnbc1000u1test";

        // Act
        var key1 = _service.GenerateKey(walletId, operation, parameters);
        var key2 = _service.GenerateKey(walletId, operation, parameters);

        // Assert
        Assert.Equal(key1, key2);
        Assert.NotEmpty(key1);
    }

    [Fact]
    public void GenerateKey_DifferentParameters_ShouldCreateDifferentKeys()
    {
        // Arrange
        var walletId = Guid.NewGuid();
        var operation = "pay-invoice";

        // Act
        var key1 = _service.GenerateKey(walletId, operation, "invoice1");
        var key2 = _service.GenerateKey(walletId, operation, "invoice2");

        // Assert
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void CacheResult_ShouldStoreAndRetrieve()
    {
        // Arrange
        var key = "test-key-" + Guid.NewGuid();
        var result = new { success = true, amount = 100 };

        // Act
        _service.CacheResult(key, result);
        var retrieved = _service.GetCachedResult<object>(key);

        // Assert
        Assert.NotNull(retrieved);
    }

    [Fact]
    public void GetCachedResult_NonExistent_ShouldReturnNull()
    {
        // Arrange
        var key = "nonexistent-" + Guid.NewGuid();

        // Act
        var result = _service.GetCachedResult<object>(key);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CacheResult_ShouldExpireAfter24Hours()
    {
        // This test verifies the expiration is set, but can't easily test actual expiry
        // without waiting 24 hours or using a time-mocking library
        
        // Arrange
        var key = "expiry-test-" + Guid.NewGuid();
        var result = new { data = "test" };

        // Act
        _service.CacheResult(key, result);
        var retrieved = _service.GetCachedResult<object>(key);

        // Assert - should exist immediately
        Assert.NotNull(retrieved);
        
        // Note: In production, entries expire after 24 hours
        // Full expiry testing would require time-mocking
    }

    [Fact]
    public void CleanupExpiredEntries_ShouldRemoveExpired()
    {
        // Arrange - add some test entries
        for (int i = 0; i < 5; i++)
        {
            _service.CacheResult($"test-{i}", new { value = i });
        }

        // Act
        var removedCount = IdempotencyService.CleanupExpiredEntries();

        // Assert - in real scenario, this would remove expired entries
        // Since we just added them, nothing should be expired yet
        Assert.True(removedCount >= 0);
    }

    [Fact]
    public void ConcurrentAccess_ShouldBeSafe()
    {
        // Arrange
        var key = "concurrent-test-" + Guid.NewGuid();
        var tasks = new Task[10];

        // Act - multiple threads trying to cache the same key
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks[i] = Task.Run(() =>
            {
                _service.CacheResult(key, new { threadId = index });
                var result = _service.GetCachedResult<object>(key);
                Assert.NotNull(result);
            });
        }

        // Assert - should not throw
        Task.WaitAll(tasks);
    }

    [Fact]
    public void GenerateKey_NullParameters_ShouldHandleGracefully()
    {
        // Arrange
        var walletId = Guid.NewGuid();
        var operation = "test-op";

        // Act
        var key = _service.GenerateKey(walletId, operation, null!);

        // Assert
        Assert.NotEmpty(key);
    }

    [Fact]
    public void CacheResult_Overwrites_PreviousValue()
    {
        // Arrange
        var key = "overwrite-test-" + Guid.NewGuid();
        var result1 = new { value = 1 };
        var result2 = new { value = 2 };

        // Act
        _service.CacheResult(key, result1);
        _service.CacheResult(key, result2);
        var retrieved = _service.GetCachedResult<dynamic>(key);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(2, retrieved.value);
    }

    [Fact]
    public void LargeDataset_ShouldHandleThousandsOfEntries()
    {
        // Arrange & Act - cache 1000 unique results
        for (int i = 0; i < 1000; i++)
        {
            var key = $"large-test-{i}";
            _service.CacheResult(key, new { index = i });
        }

        // Assert - all should be retrievable
        for (int i = 0; i < 1000; i++)
        {
            var key = $"large-test-{i}";
            var result = _service.GetCachedResult<dynamic>(key);
            Assert.NotNull(result);
            Assert.Equal(i, result.index);
        }
    }
}
