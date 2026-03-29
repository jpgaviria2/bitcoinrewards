using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BitcoinRewards.Tests.Services
{
    public class RateLimitServiceTests
    {
        private readonly RateLimitService _service;
        private readonly RateLimitPolicy _testPolicy;

        public RateLimitServiceTests()
        {
            var loggerMock = new Mock<ILogger<RateLimitService>>();
            _service = new RateLimitService(loggerMock.Object);
            
            _testPolicy = new RateLimitPolicy
            {
                PolicyId = "test",
                RequestsPerWindow = 10,
                WindowDuration = TimeSpan.FromMinutes(1),
                BurstSize = 5
            };
        }

        [Fact]
        public async Task CheckRateLimit_FirstRequest_ShouldBeAllowed()
        {
            // Arrange
            var key = "test-client-1";

            // Act
            var state = await _service.CheckRateLimitAsync(key, _testPolicy);

            // Assert
            state.Should().NotBeNull();
            state.IsLimited.Should().BeFalse();
            state.TokensRemaining.Should().Be(14); // 10 + 5 burst - 1
        }

        [Fact]
        public async Task CheckRateLimit_MultipleRequests_ShouldDecrementTokens()
        {
            // Arrange
            var key = "test-client-2";
            var requestCount = 5;

            // Act
            RateLimitState? lastState = null;
            for (int i = 0; i < requestCount; i++)
            {
                lastState = await _service.CheckRateLimitAsync(key, _testPolicy);
            }

            // Assert
            lastState.Should().NotBeNull();
            lastState!.IsLimited.Should().BeFalse();
            lastState.TokensRemaining.Should().Be(10); // 15 - 5 requests
        }

        [Fact]
        public async Task CheckRateLimit_ExceedingLimit_ShouldReturnLimited()
        {
            // Arrange
            var key = "test-client-3";
            var policy = new RateLimitPolicy
            {
                PolicyId = "strict",
                RequestsPerWindow = 2,
                WindowDuration = TimeSpan.FromMinutes(1),
                BurstSize = 1
            };

            // Act - Make 4 requests (capacity is 3)
            RateLimitState? state1 = await _service.CheckRateLimitAsync(key, policy);
            RateLimitState? state2 = await _service.CheckRateLimitAsync(key, policy);
            RateLimitState? state3 = await _service.CheckRateLimitAsync(key, policy);
            RateLimitState? state4 = await _service.CheckRateLimitAsync(key, policy);

            // Assert
            state1.IsLimited.Should().BeFalse();
            state2.IsLimited.Should().BeFalse();
            state3.IsLimited.Should().BeFalse();
            state4.IsLimited.Should().BeTrue();
            state4.TokensRemaining.Should().Be(0);
        }

        [Fact]
        public async Task CheckRateLimit_TokenRefill_ShouldAllowMoreRequests()
        {
            // Arrange
            var key = "test-client-4";
            var policy = new RateLimitPolicy
            {
                PolicyId = "refill-test",
                RequestsPerWindow = 10,
                WindowDuration = TimeSpan.FromSeconds(1), // 1 second window for fast test
                BurstSize = 2
            };

            // Act - Exhaust tokens
            for (int i = 0; i < 12; i++)
            {
                await _service.CheckRateLimitAsync(key, policy);
            }
            
            var limitedState = await _service.CheckRateLimitAsync(key, policy);
            limitedState.IsLimited.Should().BeTrue();

            // Wait for refill
            await Task.Delay(1100); // 1.1 seconds

            var afterRefillState = await _service.CheckRateLimitAsync(key, policy);

            // Assert
            afterRefillState.IsLimited.Should().BeFalse();
            afterRefillState.TokensRemaining.Should().BeGreaterThan(0);
        }

        [Fact]
        public void ResetRateLimit_ShouldClearBucket()
        {
            // Arrange
            var key = "test-client-5";
            var policyId = "test";

            // Act - Consume some tokens
            _service.CheckRateLimitAsync(key, _testPolicy).Wait();
            _service.CheckRateLimitAsync(key, _testPolicy).Wait();

            // Reset
            _service.ResetRateLimit(key, policyId);

            // Check state after reset
            var state = _service.GetRateLimitState(key, _testPolicy);

            // Assert
            state.TokensRemaining.Should().Be(15); // Full capacity restored
        }

        [Fact]
        public void GetRateLimitState_WithoutRequests_ShouldReturnFullCapacity()
        {
            // Arrange
            var key = "test-client-6";

            // Act
            var state = _service.GetRateLimitState(key, _testPolicy);

            // Assert
            state.Should().NotBeNull();
            state.TokensRemaining.Should().Be(15); // 10 + 5 burst
            state.IsLimited.Should().BeFalse();
            state.RequestCount.Should().Be(0);
        }

        [Fact]
        public async Task CheckRateLimit_DifferentKeys_ShouldBeIndependent()
        {
            // Arrange
            var key1 = "client-a";
            var key2 = "client-b";

            // Act
            for (int i = 0; i < 10; i++)
            {
                await _service.CheckRateLimitAsync(key1, _testPolicy);
            }

            var state1 = await _service.CheckRateLimitAsync(key1, _testPolicy);
            var state2 = await _service.CheckRateLimitAsync(key2, _testPolicy);

            // Assert
            state1.TokensRemaining.Should().Be(4); // 15 - 11
            state2.TokensRemaining.Should().Be(14); // Full - 1 (first request)
        }

        [Fact]
        public async Task CheckRateLimit_DifferentPolicies_ShouldBeIndependent()
        {
            // Arrange
            var key = "test-client-7";
            var policy1 = new RateLimitPolicy
            {
                PolicyId = "policy-a",
                RequestsPerWindow = 5,
                WindowDuration = TimeSpan.FromMinutes(1),
                BurstSize = 2
            };
            var policy2 = new RateLimitPolicy
            {
                PolicyId = "policy-b",
                RequestsPerWindow = 10,
                WindowDuration = TimeSpan.FromMinutes(1),
                BurstSize = 5
            };

            // Act
            for (int i = 0; i < 5; i++)
            {
                await _service.CheckRateLimitAsync(key, policy1);
            }

            var state1 = await _service.CheckRateLimitAsync(key, policy1);
            var state2 = await _service.CheckRateLimitAsync(key, policy2);

            // Assert
            state1.TokensRemaining.Should().Be(1); // Policy A nearly exhausted
            state2.TokensRemaining.Should().Be(14); // Policy B still full
        }

        [Fact]
        public void GetStatistics_ShouldReturnCorrectCounts()
        {
            // Arrange
            var keys = new[] { "client-1", "client-2", "client-3" };

            // Act - Create buckets
            foreach (var key in keys)
            {
                _service.CheckRateLimitAsync(key, _testPolicy).Wait();
            }

            var stats = _service.GetStatistics();

            // Assert
            stats.Should().NotBeNull();
            stats.TotalBuckets.Should().BeGreaterThanOrEqualTo(3);
        }

        [Fact]
        public async Task CheckRateLimit_ConcurrentRequests_ShouldBeThreadSafe()
        {
            // Arrange
            var key = "concurrent-client";
            var policy = new RateLimitPolicy
            {
                PolicyId = "concurrent",
                RequestsPerWindow = 100,
                WindowDuration = TimeSpan.FromMinutes(1),
                BurstSize = 50
            };
            var concurrentRequests = 10;

            // Act - Make concurrent requests
            var tasks = new Task<RateLimitState>[concurrentRequests];
            for (int i = 0; i < concurrentRequests; i++)
            {
                tasks[i] = _service.CheckRateLimitAsync(key, policy);
            }

            await Task.WhenAll(tasks);

            var finalState = _service.GetRateLimitState(key, policy);

            // Assert - All tokens should be properly consumed
            finalState.TokensRemaining.Should().Be(140); // 150 - 10
        }

        [Fact]
        public async Task CheckRateLimit_ResetAt_ShouldBeInFuture()
        {
            // Arrange
            var key = "test-client-8";

            // Act
            var state = await _service.CheckRateLimitAsync(key, _testPolicy);

            // Assert
            var resetTime = DateTimeOffset.FromUnixTimeSeconds(state.ResetAt);
            resetTime.Should().BeAfter(DateTimeOffset.UtcNow);
            resetTime.Should().BeCloseTo(DateTimeOffset.UtcNow.Add(_testPolicy.WindowDuration), TimeSpan.FromSeconds(5));
        }
    }
}
