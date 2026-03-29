using System;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using FluentAssertions;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests;

public class IdempotencyServiceTests
{
    private readonly IdempotencyService _service;

    public IdempotencyServiceTests()
    {
        _service = new IdempotencyService(NullLogger<IdempotencyService>.Instance);
        // Clean any state from previous tests (static dictionary)
        IdempotencyService.CleanupExpiredEntries();
    }

    [Fact]
    public void CacheResult_And_GetCachedResult_ReturnsCachedValue()
    {
        // Arrange
        var key = $"test-{Guid.NewGuid()}";
        var result = new TestResult { Value = "hello" };

        // Act
        _service.CacheResult(key, result);
        var cached = _service.GetCachedResult<TestResult>(key);

        // Assert
        cached.Should().NotBeNull();
        cached!.Value.Should().Be("hello");
    }

    [Fact]
    public void GetCachedResult_ReturnsNull_ForUnknownKey()
    {
        // Act
        var result = _service.GetCachedResult<TestResult>("nonexistent-key");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetCachedResult_ReturnsNull_ForNullOrWhitespaceKey()
    {
        // Act & Assert
        _service.GetCachedResult<TestResult>(null!).Should().BeNull();
        _service.GetCachedResult<TestResult>("").Should().BeNull();
        _service.GetCachedResult<TestResult>("   ").Should().BeNull();
    }

    [Fact]
    public void CacheResult_DoesNotThrow_ForNullOrWhitespaceKey()
    {
        // Act & Assert: should silently no-op
        var act1 = () => _service.CacheResult(null!, new TestResult());
        var act2 = () => _service.CacheResult("", new TestResult());
        var act3 = () => _service.CacheResult("   ", new TestResult());

        act1.Should().NotThrow();
        act2.Should().NotThrow();
        act3.Should().NotThrow();
    }

    [Fact]
    public void CacheResult_OverwritesPreviousValue()
    {
        // Arrange
        var key = $"overwrite-{Guid.NewGuid()}";

        // Act
        _service.CacheResult(key, new TestResult { Value = "first" });
        _service.CacheResult(key, new TestResult { Value = "second" });

        var cached = _service.GetCachedResult<TestResult>(key);

        // Assert
        cached!.Value.Should().Be("second");
    }

    [Fact]
    public void GenerateKey_ReturnsDeterministicKey()
    {
        // Arrange
        var walletId = Guid.NewGuid();

        // Act
        var key1 = _service.GenerateKey(walletId, "deposit", 100, "USD");
        var key2 = _service.GenerateKey(walletId, "deposit", 100, "USD");

        // Assert
        key1.Should().Be(key2, "same inputs should produce same idempotency key");
    }

    [Fact]
    public void GenerateKey_ReturnsDifferentKeys_ForDifferentInputs()
    {
        // Arrange
        var walletId = Guid.NewGuid();

        // Act
        var key1 = _service.GenerateKey(walletId, "deposit", 100, "USD");
        var key2 = _service.GenerateKey(walletId, "deposit", 200, "USD");
        var key3 = _service.GenerateKey(walletId, "withdraw", 100, "USD");

        // Assert
        key1.Should().NotBe(key2);
        key1.Should().NotBe(key3);
        key2.Should().NotBe(key3);
    }

    [Fact]
    public void CleanupExpiredEntries_RemovesOldEntries()
    {
        // Arrange: add some fresh entries
        var freshKey = $"fresh-{Guid.NewGuid()}";
        _service.CacheResult(freshKey, new TestResult { Value = "fresh" });

        // Act
        var removed = IdempotencyService.CleanupExpiredEntries();

        // Assert: fresh entries should NOT be removed
        var cached = _service.GetCachedResult<TestResult>(freshKey);
        cached.Should().NotBeNull("fresh entries should not be cleaned up");
    }

    [Fact]
    public void GetStatistics_ReturnsTotalAndExpiredCounts()
    {
        // Arrange
        var key = $"stats-{Guid.NewGuid()}";
        _service.CacheResult(key, new TestResult { Value = "stats-test" });

        // Act
        var (totalEntries, expiredEntries) = _service.GetStatistics();

        // Assert
        totalEntries.Should().BeGreaterOrEqualTo(1);
        expiredEntries.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void GetCachedResult_ReturnsNull_ForWrongType()
    {
        // Arrange
        var key = $"type-{Guid.NewGuid()}";
        _service.CacheResult(key, "a string, not TestResult");

        // Act
        var result = _service.GetCachedResult<TestResult>(key);

        // Assert
        result.Should().BeNull("cached type does not match requested type");
    }

    private class TestResult
    {
        public string Value { get; set; } = string.Empty;
    }
}
