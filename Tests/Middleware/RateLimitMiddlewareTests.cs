using System;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Middleware;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests.Middleware;

/// <summary>
/// Tests for RateLimitMiddleware - DOS protection
/// </summary>
public class RateLimitMiddlewareTests
{
    [Fact]
    public async Task RateLimit_UnderLimit_ShouldAllow()
    {
        // Arrange
        var middleware = new RateLimitMiddleware(next => Task.CompletedTask);
        var context = CreateHttpContext("/plugins/bitcoin-rewards/wallet/123/balance");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task RateLimit_ExceedLimit_ShouldReturn429()
    {
        // Arrange
        var middleware = new RateLimitMiddleware(next => Task.CompletedTask);
        var walletId = Guid.NewGuid().ToString();
        var path = $"/plugins/bitcoin-rewards/wallet/{walletId}/pay-invoice";

        // Act - make 21 requests (limit is 20/min)
        HttpContext? lastContext = null;
        for (int i = 0; i < 21; i++)
        {
            lastContext = CreateHttpContext(path);
            await middleware.InvokeAsync(lastContext);
        }

        // Assert - 21st request should be rate limited
        Assert.Equal(429, lastContext!.Response.StatusCode);
    }

    [Fact]
    public async Task RateLimit_DifferentWallets_ShouldNotInterfere()
    {
        // Arrange
        var middleware = new RateLimitMiddleware(next => Task.CompletedTask);
        var wallet1 = Guid.NewGuid().ToString();
        var wallet2 = Guid.NewGuid().ToString();

        // Act - 10 requests to each wallet
        for (int i = 0; i < 10; i++)
        {
            var ctx1 = CreateHttpContext($"/plugins/bitcoin-rewards/wallet/{wallet1}/pay-invoice");
            var ctx2 = CreateHttpContext($"/plugins/bitcoin-rewards/wallet/{wallet2}/pay-invoice");
            
            await middleware.InvokeAsync(ctx1);
            await middleware.InvokeAsync(ctx2);
        }

        // Assert - both should still be under limit (20/min each)
        var testCtx = CreateHttpContext($"/plugins/bitcoin-rewards/wallet/{wallet1}/pay-invoice");
        await middleware.InvokeAsync(testCtx);
        Assert.NotEqual(429, testCtx.Response.StatusCode);
    }

    [Fact]
    public async Task RateLimit_WalletCreation_ShouldLimitPerIP()
    {
        // Arrange
        var middleware = new RateLimitMiddleware(next => Task.CompletedTask);
        var path = "/plugins/bitcoin-rewards/wallet/create";
        var ip = "192.168.1.100";

        // Act - make 6 requests (limit is 5/hour per IP)
        HttpContext? lastContext = null;
        for (int i = 0; i < 6; i++)
        {
            lastContext = CreateHttpContext(path, ip);
            await middleware.InvokeAsync(lastContext);
        }

        // Assert
        Assert.Equal(429, lastContext!.Response.StatusCode);
    }

    [Fact]
    public void CleanupExpiredHistories_ShouldNotThrow()
    {
        // Act & Assert
        RateLimitMiddleware.CleanupExpiredHistories();
        // Should complete without exception
    }

    [Fact]
    public async Task RateLimit_NonWalletEndpoint_ShouldPassThrough()
    {
        // Arrange
        var middleware = new RateLimitMiddleware(next => Task.CompletedTask);
        var context = CreateHttpContext("/some/other/endpoint");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);
    }

    private static HttpContext CreateHttpContext(string path, string? remoteIp = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "POST";
        context.Response.Body = new System.IO.MemoryStream();
        
        if (remoteIp != null)
        {
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);
        }
        else
        {
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");
        }

        return context;
    }
}
