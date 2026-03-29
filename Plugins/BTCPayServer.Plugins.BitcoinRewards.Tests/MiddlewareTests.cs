using System;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Middleware;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using FluentAssertions;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests;

public class CorrelationIdMiddlewareTests
{
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddlewareTests()
    {
        _logger = NullLogger<CorrelationIdMiddleware>.Instance;
    }

    [Fact]
    public async Task InvokeAsync_SetsCorrelationIdInContextItems()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new CorrelationIdMiddleware(next, _logger);
        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue("middleware must always call _next()");
        context.Items["CorrelationId"].Should().NotBeNull(
            "middleware should set correlation ID in context items");
        context.Items["CorrelationId"]!.ToString().Should().NotBeNullOrEmpty(
            "correlation ID should not be empty");
    }

    [Fact]
    public async Task InvokeAsync_UsesExistingCorrelationIdFromRequest()
    {
        // Arrange
        var expectedId = "my-custom-correlation-id";
        RequestDelegate next = _ => Task.CompletedTask;

        var middleware = new CorrelationIdMiddleware(next, _logger);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = expectedId;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Items["CorrelationId"].Should().Be(expectedId,
            "middleware should use existing correlation ID from request headers");
    }

    [Fact]
    public async Task InvokeAsync_GeneratesNewCorrelationIdWhenNoneProvided()
    {
        // Arrange
        RequestDelegate next = _ => Task.CompletedTask;

        var middleware = new CorrelationIdMiddleware(next, _logger);
        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Items["CorrelationId"].Should().NotBeNull();
        context.Items["CorrelationId"]!.ToString().Should().HaveLength(13,
            "generated correlation IDs should be 13-character short GUIDs");
    }

    [Fact]
    public async Task InvokeAsync_AlwaysCallsNext_EvenOnException()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new CorrelationIdMiddleware(next, _logger);

        // Use a context where request headers throw (simulate failure)
        var mockContext = new Mock<HttpContext>();
        var mockRequest = new Mock<HttpRequest>();
        var mockResponse = new Mock<HttpResponse>();
        var mockHeaders = new Mock<IHeaderDictionary>();

        // Force TryGetValue to throw
        mockRequest.Setup(r => r.Headers).Throws(new InvalidOperationException("Header access failed"));
        mockResponse.Setup(r => r.Headers).Returns(new HeaderDictionary());
        mockResponse.Setup(r => r.OnStarting(It.IsAny<Func<Task>>()));
        mockContext.Setup(c => c.Request).Returns(mockRequest.Object);
        mockContext.Setup(c => c.Response).Returns(mockResponse.Object);
        mockContext.Setup(c => c.Items).Returns(new Dictionary<object, object?>());

        // Act
        await middleware.InvokeAsync(mockContext.Object);

        // Assert
        nextCalled.Should().BeTrue("middleware must ALWAYS call _next() even if correlation ID setup fails");
    }

    [Fact]
    public async Task InvokeAsync_DoesNotCrash_WhenHeadersThrow()
    {
        // Arrange
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new CorrelationIdMiddleware(next, _logger);

        var mockContext = new Mock<HttpContext>();
        var mockRequest = new Mock<HttpRequest>();
        var mockResponse = new Mock<HttpResponse>();

        mockRequest.Setup(r => r.Headers).Throws(new Exception("Simulated failure"));
        mockResponse.Setup(r => r.Headers).Returns(new HeaderDictionary());
        mockResponse.Setup(r => r.OnStarting(It.IsAny<Func<Task>>()));
        mockContext.Setup(c => c.Request).Returns(mockRequest.Object);
        mockContext.Setup(c => c.Response).Returns(mockResponse.Object);
        mockContext.Setup(c => c.Items).Returns(new Dictionary<object, object?>());

        // Act & Assert: must not throw
        var act = async () => await middleware.InvokeAsync(mockContext.Object);
        await act.Should().NotThrowAsync("middleware must never crash the request pipeline");
    }

    [Fact]
    public async Task InvokeAsync_StoresCorrelationIdInHttpContextItems()
    {
        // Arrange
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new CorrelationIdMiddleware(next, _logger);
        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Items.Should().ContainKey("CorrelationId");
        context.Items["CorrelationId"].Should().NotBeNull();
    }

    [Fact]
    public void GetCorrelationId_ReturnsStoredValue()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Items["CorrelationId"] = "test-id-123";

        // Act
        var result = context.GetCorrelationId();

        // Assert
        result.Should().Be("test-id-123");
    }

    [Fact]
    public void GetCorrelationId_ReturnsNull_WhenNotSet()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        var result = context.GetCorrelationId();

        // Assert
        result.Should().BeNull();
    }
}

public class RateLimitingMiddlewareTests
{
    private readonly ILogger<RateLimitingMiddleware> _logger;

    public RateLimitingMiddlewareTests()
    {
        _logger = NullLogger<RateLimitingMiddleware>.Instance;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/stores")]
    [InlineData("/server/settings")]
    [InlineData("/some-other-path")]
    public async Task InvokeAsync_PassesThroughNonPluginPaths(string path)
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new RateLimitingMiddleware(next, _logger);
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        // Act — for non-plugin paths, rate limiting services are not needed
        await middleware.InvokeAsync(
            context,
            rateLimitService: null!,
            storeRepository: null!,
            metrics: null!);

        // Assert
        nextCalled.Should().BeTrue("non-plugin paths should pass through without rate limiting");
    }

    [Theory]
    [InlineData("/plugins/bitcoin-rewards/some-store/settings")]
    [InlineData("/api/v1/bitcoin-rewards/endpoint")]
    public async Task InvokeAsync_DoesNotCrash_WhenServicesAreNull(string path)
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new RateLimitingMiddleware(next, _logger);
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        // Act — simulate DI failure by passing null services
        // The try/catch in InvokeAsync should catch the NullReferenceException
        var act = async () => await middleware.InvokeAsync(
            context,
            rateLimitService: null!,
            storeRepository: null!,
            metrics: null!);

        // Assert: must not throw (try/catch should catch)
        await act.Should().NotThrowAsync(
            "middleware must never crash the request pipeline, even if DI fails");
        nextCalled.Should().BeTrue(
            "middleware must always call _next() on error");
    }

    [Fact]
    public async Task InvokeAsync_AlwaysCallsNext_OnError()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new RateLimitingMiddleware(next, _logger);
        var context = new DefaultHttpContext();
        context.Request.Path = "/plugins/bitcoin-rewards/test-store/webhooks/square";

        // Act — services are null, triggering the catch block
        await middleware.InvokeAsync(
            context,
            rateLimitService: null!,
            storeRepository: null!,
            metrics: null!);

        // Assert
        nextCalled.Should().BeTrue("middleware MUST always call _next() even on error");
    }

    [Fact]
    public async Task InvokeAsync_AllowsRequest_WhenStoreRepoThrows()
    {
        // Arrange - StoreRepository can't be mocked (non-virtual methods),
        // so we verify the try/catch in middleware handles the failure gracefully
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new RateLimitingMiddleware(next, _logger);
        var context = new DefaultHttpContext();
        context.Request.Path = "/plugins/bitcoin-rewards/test-store/settings";

        var rateLimitService = new RateLimitService(NullLogger<RateLimitService>.Instance);
        var metrics = new RewardMetrics();

        // Pass null StoreRepository — will cause NullReferenceException inside middleware,
        // which the try/catch should handle and still call _next()
        await middleware.InvokeAsync(context, rateLimitService, null!, metrics);

        // Assert - middleware should catch the error and still call next
        nextCalled.Should().BeTrue("middleware must call _next() even when StoreRepository fails");
    }
}
