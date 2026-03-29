using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Exceptions;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using FluentAssertions;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests;

/// <summary>
/// Tests for ErrorTrackingService using an in-memory database.
/// </summary>
public class ErrorTrackingServiceTests : IDisposable
{
    private readonly BitcoinRewardsPluginDbContext _dbContext;
    private readonly Mock<BitcoinRewardsPluginDbContextFactory> _mockFactory;
    private readonly ErrorTrackingService _service;
    private readonly string _dbName;

    public ErrorTrackingServiceTests()
    {
        _dbName = $"TestDb_{Guid.NewGuid()}";

        var options = new DbContextOptionsBuilder<BitcoinRewardsPluginDbContext>()
            .UseInMemoryDatabase(databaseName: _dbName)
            .Options;

        _dbContext = new BitcoinRewardsPluginDbContext(options);
        _dbContext.Database.EnsureCreated();

        _mockFactory = new Mock<BitcoinRewardsPluginDbContextFactory>(MockBehavior.Loose, (object)null!);
        _mockFactory.Setup(f => f.CreateContext(null)).Returns(() =>
        {
            // Each call creates a new context pointing to the same in-memory DB
            var opts = new DbContextOptionsBuilder<BitcoinRewardsPluginDbContext>()
                .UseInMemoryDatabase(databaseName: _dbName)
                .Options;
            return new BitcoinRewardsPluginDbContext(opts);
        });

        _service = new ErrorTrackingService(
            _mockFactory.Object,
            NullLogger<ErrorTrackingService>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task LogErrorAsync_SavesErrorToDatabase()
    {
        // Arrange
        var exception = new BitcoinRewardsException(
            RewardErrorType.SquareApiError,
            "Square API returned 500")
            .ForOrder("order-123")
            .ForStore("store-456");

        // Act
        var error = await _service.LogErrorAsync(exception, "user-1");

        // Assert
        error.Should().NotBeNull();
        error.Id.Should().NotBeNullOrEmpty();
        error.ErrorType.Should().Be("SquareApiError");
        error.Message.Should().Be("Square API returned 500");
        error.OrderId.Should().Be("order-123");
        error.StoreId.Should().Be("store-456");
        error.UserId.Should().Be("user-1");
        error.Resolved.Should().BeFalse();
    }

    [Fact]
    public async Task LogExceptionAsync_SavesGenericExceptionToDatabase()
    {
        // Arrange
        var exception = new InvalidOperationException("Something went wrong");

        // Act
        var error = await _service.LogExceptionAsync(
            exception,
            RewardErrorType.DatabaseWriteFailed,
            orderId: "order-789",
            storeId: "store-abc",
            userId: "user-2",
            context: new Dictionary<string, object> { { "detail", "test" } });

        // Assert
        error.Should().NotBeNull();
        error.ErrorType.Should().Be("DatabaseWriteFailed");
        error.Message.Should().Be("Something went wrong");
        error.OrderId.Should().Be("order-789");
        error.StoreId.Should().Be("store-abc");
        error.UserId.Should().Be("user-2");
        error.Context.Should().Contain("detail");
    }

    [Fact]
    public async Task GetRecentErrorsAsync_ReturnsLoggedErrors()
    {
        // Arrange
        await SeedErrorAsync("SquareApiError", "store-1");
        await SeedErrorAsync("DatabaseWriteFailed", "store-1");
        await SeedErrorAsync("SquareApiError", "store-2");

        // Act
        var errors = await _service.GetRecentErrorsAsync(limit: 10);

        // Assert
        errors.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetRecentErrorsAsync_FiltersbyStoreId()
    {
        // Arrange
        await SeedErrorAsync("SquareApiError", "store-A");
        await SeedErrorAsync("SquareApiError", "store-B");

        // Act
        var errors = await _service.GetRecentErrorsAsync(storeId: "store-A");

        // Assert
        errors.Should().HaveCount(1);
        errors[0].StoreId.Should().Be("store-A");
    }

    [Fact]
    public async Task GetErrorStatisticsAsync_ReturnsCorrectCounts()
    {
        // Arrange
        await SeedErrorAsync("SquareApiError", "store-1");
        await SeedErrorAsync("SquareApiError", "store-1");
        await SeedErrorAsync("DatabaseWriteFailed", "store-1");

        // Act
        var stats = await _service.GetErrorStatisticsAsync(storeId: "store-1");

        // Assert
        stats.TotalErrors.Should().Be(3);
        stats.UnresolvedErrors.Should().Be(3);
        stats.ResolvedErrors.Should().Be(0);
        stats.ErrorsByType.Should().ContainKey("SquareApiError").WhoseValue.Should().Be(2);
        stats.ErrorsByType.Should().ContainKey("DatabaseWriteFailed").WhoseValue.Should().Be(1);
        stats.MostCommonError.Should().Be("SquareApiError");
    }

    [Fact]
    public async Task ResolveErrorAsync_MarksErrorAsResolved()
    {
        // Arrange
        var error = await SeedErrorAsync("SquareApiError", "store-1");

        // Act
        var result = await _service.ResolveErrorAsync(error.Id, "admin", "Fixed the issue");

        // Assert
        result.Should().BeTrue();

        var stats = await _service.GetErrorStatisticsAsync();
        stats.ResolvedErrors.Should().Be(1);
    }

    [Fact]
    public async Task ResolveErrorAsync_ReturnsFalse_ForNonexistentError()
    {
        // Act
        var result = await _service.ResolveErrorAsync("nonexistent-id");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RecordRetryAttemptAsync_IncrementsRetryCount()
    {
        // Arrange
        var error = await SeedErrorAsync("LightningNodeUnreachable", "store-1");

        // Act
        var result1 = await _service.RecordRetryAttemptAsync(error.Id);
        var result2 = await _service.RecordRetryAttemptAsync(error.Id);

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeTrue();
    }

    [Fact]
    public async Task RecordRetryAttemptAsync_ReturnsFalse_ForNonexistentError()
    {
        // Act
        var result = await _service.RecordRetryAttemptAsync("nonexistent-id");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetRetryableErrorsAsync_ReturnsOnlyRetryableTypes()
    {
        // Arrange
        await SeedErrorAsync("LightningNodeUnreachable", "store-1");
        await SeedErrorAsync("PluginNotEnabled", "store-1"); // Not retryable

        // Act
        var retryable = await _service.GetRetryableErrorsAsync();

        // Assert
        retryable.Should().HaveCount(1);
        retryable[0].ErrorType.Should().Be("LightningNodeUnreachable");
    }

    private async Task<RewardError> SeedErrorAsync(string errorType, string storeId)
    {
        var error = new RewardError
        {
            Id = Guid.NewGuid().ToString(),
            ErrorType = errorType,
            Message = $"Test error: {errorType}",
            StoreId = storeId,
            Timestamp = DateTime.UtcNow,
            Resolved = false
        };

        _dbContext.RewardErrors.Add(error);
        await _dbContext.SaveChangesAsync();
        return error;
    }
}
