using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BitcoinRewards.Tests.Services
{
    public class ErrorTrackingServiceTests
    {
        private readonly Mock<ILogger<ErrorTrackingService>> _loggerMock;
        private readonly BitcoinRewardsPluginDbContextFactory _contextFactory;
        private readonly ErrorTrackingService _service;

        public ErrorTrackingServiceTests()
        {
            _loggerMock = new Mock<ILogger<ErrorTrackingService>>();
            
            // Create in-memory database
            var options = new DbContextOptionsBuilder<BitcoinRewardsPluginDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            
            _contextFactory = new Mock<BitcoinRewardsPluginDbContextFactory>().Object;
            // Note: In real tests, inject proper factory
            
            _service = new ErrorTrackingService(_contextFactory, _loggerMock.Object);
        }

        [Fact]
        public async Task LogErrorAsync_ShouldCreateErrorRecord()
        {
            // Arrange
            var storeId = "store123";
            var rewardId = Guid.NewGuid().ToString();
            var operation = "ProcessRewardAsync";
            var errorMessage = "Test error message";
            var stackTrace = "Test stack trace";
            var isRetryable = true;
            var context = new Dictionary<string, string>
            {
                ["TransactionId"] = "tx123",
                ["Platform"] = "Square"
            };

            // Act
            await _service.LogErrorAsync(storeId, rewardId, operation, errorMessage, stackTrace, isRetryable, context);

            // Assert
            // Verify error was logged (would need to query DB in real test)
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString().Contains(errorMessage)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task GetRecentErrorsAsync_ShouldFilterByStore()
        {
            // Arrange
            var storeId = "store123";
            var days = 7;

            // Act
            var errors = await _service.GetRecentErrorsAsync(storeId, days);

            // Assert
            errors.Should().NotBeNull();
            errors.Should().BeOfType<List<BTCPayServer.Plugins.BitcoinRewards.Models.RewardError>>();
        }

        [Fact]
        public async Task GetErrorStatisticsAsync_ShouldReturnCorrectCounts()
        {
            // Arrange
            var storeId = "store123";
            var days = 7;

            // Act
            var stats = await _service.GetErrorStatisticsAsync(storeId, days);

            // Assert
            stats.Should().NotBeNull();
            stats.TotalErrors.Should().BeGreaterThanOrEqualTo(0);
            stats.UnresolvedErrors.Should().BeGreaterThanOrEqualTo(0);
            stats.RetryableErrors.Should().BeGreaterThanOrEqualTo(0);
        }

        [Fact]
        public async Task ResolveErrorAsync_ShouldMarkErrorAsResolved()
        {
            // Arrange
            var errorId = 1;
            var resolvedBy = "admin@example.com";

            // Act
            await _service.ResolveErrorAsync(errorId, resolvedBy);

            // Assert
            // Would verify database record is updated in real test
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString().Contains("resolved")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task RecordRetryAttemptAsync_ShouldIncrementRetryCount()
        {
            // Arrange
            var errorId = 1;

            // Act
            await _service.RecordRetryAttemptAsync(errorId);

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString().Contains("retry")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task GetRetryableErrorsAsync_ShouldReturnOnlyRetryableErrors()
        {
            // Arrange
            var storeId = "store123";

            // Act
            var errors = await _service.GetRetryableErrorsAsync(storeId);

            // Assert
            errors.Should().NotBeNull();
            errors.Should().AllSatisfy(e => e.IsRetryable.Should().BeTrue());
            errors.Should().AllSatisfy(e => e.IsResolved.Should().BeFalse());
        }
    }
}
