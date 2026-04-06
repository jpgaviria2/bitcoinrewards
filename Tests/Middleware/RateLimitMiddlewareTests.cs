using System;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests.Middleware;

/// <summary>
/// Tests for RateLimitingMiddleware - DOS protection.
/// Note: InvokeAsync requires DI-injected services (RateLimitService, StoreRepository,
/// RewardMetrics) which make these unit tests integration tests. These are skipped
/// until a proper test host / integration test setup is in place.
/// </summary>
public class RateLimitMiddlewareTests
{
    private static RateLimitingMiddleware CreateMiddleware() =>
        new RateLimitingMiddleware(_ => Task.CompletedTask, new Mock<ILogger<RateLimitingMiddleware>>().Object);

    [Fact(Skip = "Requires DI-injected services (RateLimitService, StoreRepository, RewardMetrics)")]
    public async Task RateLimit_UnderLimit_ShouldAllow()
    {
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires DI-injected services (RateLimitService, StoreRepository, RewardMetrics)")]
    public async Task RateLimit_ExceedLimit_ShouldReturn429()
    {
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires DI-injected services (RateLimitService, StoreRepository, RewardMetrics)")]
    public async Task RateLimit_DifferentWallets_ShouldNotInterfere()
    {
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires DI-injected services (RateLimitService, StoreRepository, RewardMetrics)")]
    public async Task RateLimit_WalletCreation_ShouldLimitPerIP()
    {
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires DI-injected services (RateLimitService, StoreRepository, RewardMetrics)")]
    public async Task RateLimit_NonWalletEndpoint_ShouldPassThrough()
    {
        await Task.CompletedTask;
    }
}
