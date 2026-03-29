using System;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using FluentAssertions;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests;

public class RateLimitServiceTests
{
    private readonly RateLimitService _service;

    public RateLimitServiceTests()
    {
        _service = new RateLimitService(NullLogger<RateLimitService>.Instance);
    }

    [Fact]
    public async Task CheckRateLimitAsync_AllowsRequest_UnderLimit()
    {
        // Arrange
        var policy = new RateLimitPolicy
        {
            PolicyId = "test",
            RequestsPerWindow = 10,
            WindowDuration = TimeSpan.FromMinutes(1),
            BurstSize = 5
        };

        // Act
        var state = await _service.CheckRateLimitAsync("client1", policy);

        // Assert
        state.IsLimited.Should().BeFalse("first request should be allowed");
        state.Key.Should().Be("client1");
        // Note: RequestCount in the returned state reflects pre-increment count
        state.TokensRemaining.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task CheckRateLimitAsync_BlocksRequest_WhenLimitExceeded()
    {
        // Arrange
        var policy = new RateLimitPolicy
        {
            PolicyId = "test-exceed",
            RequestsPerWindow = 3,
            WindowDuration = TimeSpan.FromMinutes(1),
            BurstSize = 0 // No burst allowance
        };

        // Act: exhaust all tokens
        for (int i = 0; i < 3; i++)
        {
            var allowed = await _service.CheckRateLimitAsync("exhaust-client", policy);
            allowed.IsLimited.Should().BeFalse($"request {i + 1} should be allowed");
        }

        // The next request should be blocked
        var blocked = await _service.CheckRateLimitAsync("exhaust-client", policy);

        // Assert
        blocked.IsLimited.Should().BeTrue("request beyond limit should be blocked");
        blocked.TokensRemaining.Should().Be(0);
    }

    [Fact]
    public async Task CheckRateLimitAsync_BurstAllowsExtraRequests()
    {
        // Arrange
        var policy = new RateLimitPolicy
        {
            PolicyId = "test-burst",
            RequestsPerWindow = 2,
            WindowDuration = TimeSpan.FromMinutes(1),
            BurstSize = 3 // Allow 3 extra requests
        };

        // Act: should allow 2 + 3 = 5 requests total
        for (int i = 0; i < 5; i++)
        {
            var state = await _service.CheckRateLimitAsync("burst-client", policy);
            state.IsLimited.Should().BeFalse($"request {i + 1} should be allowed (within burst)");
        }

        // The 6th request should be blocked
        var blocked = await _service.CheckRateLimitAsync("burst-client", policy);
        blocked.IsLimited.Should().BeTrue("request beyond burst should be blocked");
    }

    [Fact]
    public async Task CheckRateLimitAsync_DifferentKeys_TrackSeparately()
    {
        // Arrange
        var policy = new RateLimitPolicy
        {
            PolicyId = "test-separate",
            RequestsPerWindow = 1,
            WindowDuration = TimeSpan.FromMinutes(1),
            BurstSize = 0
        };

        // Act
        var state1 = await _service.CheckRateLimitAsync("clientA", policy);
        var state2 = await _service.CheckRateLimitAsync("clientB", policy);

        // Assert
        state1.IsLimited.Should().BeFalse();
        state2.IsLimited.Should().BeFalse("different keys should have independent buckets");
    }

    [Fact]
    public async Task CheckRateLimitAsync_TokensRefillOverTime()
    {
        // Arrange
        var policy = new RateLimitPolicy
        {
            PolicyId = "test-refill",
            RequestsPerWindow = 100,
            WindowDuration = TimeSpan.FromSeconds(1),
            BurstSize = 0
        };

        // Act: exhaust tokens
        for (int i = 0; i < 100; i++)
        {
            await _service.CheckRateLimitAsync("refill-client", policy);
        }

        var blocked = await _service.CheckRateLimitAsync("refill-client", policy);
        blocked.IsLimited.Should().BeTrue("should be blocked after exhaustion");

        // Wait for refill (1 second window with 100 req/s refill rate)
        await Task.Delay(1100);

        var allowed = await _service.CheckRateLimitAsync("refill-client", policy);

        // Assert
        allowed.IsLimited.Should().BeFalse("tokens should have refilled after waiting");
    }

    [Fact]
    public async Task ResetRateLimit_RemovesBucket()
    {
        // Arrange: create a bucket by checking rate limit
        var policy = new RateLimitPolicy
        {
            PolicyId = "test-reset",
            RequestsPerWindow = 10,
            WindowDuration = TimeSpan.FromMinutes(1),
            BurstSize = 0
        };

        await _service.CheckRateLimitAsync("reset-client", policy);

        // Act
        _service.ResetRateLimit("reset-client", "test-reset");

        // Assert: state should be fresh
        var state = _service.GetRateLimitState("reset-client", policy);
        state.RequestCount.Should().Be(0, "bucket should have been removed and state should be fresh");
    }

    [Fact]
    public void GetRateLimitState_ReturnsDefaultState_ForNewKey()
    {
        // Arrange
        var policy = new RateLimitPolicy
        {
            PolicyId = "test-new",
            RequestsPerWindow = 50,
            WindowDuration = TimeSpan.FromMinutes(1),
            BurstSize = 10
        };

        // Act
        var state = _service.GetRateLimitState("new-client", policy);

        // Assert
        state.IsLimited.Should().BeFalse();
        state.TokensRemaining.Should().Be(60); // 50 + 10 burst
        state.RequestCount.Should().Be(0);
        state.Key.Should().Be("new-client");
    }

    [Fact]
    public async Task GetStatistics_ReturnsCorrectBucketCount()
    {
        // Arrange
        var policy = new RateLimitPolicy
        {
            PolicyId = "test-stats",
            RequestsPerWindow = 10,
            WindowDuration = TimeSpan.FromMinutes(1),
            BurstSize = 0
        };

        var statsBefore = _service.GetStatistics();
        var bucketsBefore = statsBefore.TotalBuckets;

        // Act
        await _service.CheckRateLimitAsync("stats-client1", policy);
        await _service.CheckRateLimitAsync("stats-client2", policy);

        // Assert
        var statsAfter = _service.GetStatistics();
        statsAfter.TotalBuckets.Should().Be(bucketsBefore + 2);
    }

    [Fact]
    public async Task GetStatistics_TracksLimitedKeys()
    {
        // Arrange
        var policy = new RateLimitPolicy
        {
            PolicyId = "test-limited-stats",
            RequestsPerWindow = 1,
            WindowDuration = TimeSpan.FromMinutes(1),
            BurstSize = 0
        };

        // Exhaust one client's tokens
        await _service.CheckRateLimitAsync("limited-client", policy);
        await _service.CheckRateLimitAsync("limited-client", policy); // this one gets limited

        // Assert
        var stats = _service.GetStatistics();
        stats.LimitedKeys.Should().BeGreaterOrEqualTo(1);
    }
}
