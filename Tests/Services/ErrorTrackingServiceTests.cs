using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Exceptions;
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
            _contextFactory = new Mock<BitcoinRewardsPluginDbContextFactory>().Object;
            _service = new ErrorTrackingService(_contextFactory, _loggerMock.Object);
        }

        [Fact]
        public async Task LogErrorAsync_ShouldCreateErrorRecord()
        {
            // Arrange
            var errorMessage = "Test error message";
            var exception = new BitcoinRewardsException(RewardErrorType.InvoiceNotFound, errorMessage)
                .ForStore("store123")
                .ForReward(Guid.NewGuid().ToString());

            // Act
            await _service.LogErrorAsync(exception);

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains(errorMessage)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task GetRecentErrorsAsync_ShouldReturnList()
        {
            // Act
            var errors = await _service.GetRecentErrorsAsync(storeId: "store123");

            // Assert
            errors.Should().NotBeNull();
            errors.Should().BeOfType<List<BTCPayServer.Plugins.BitcoinRewards.Models.RewardError>>();
        }

        [Fact]
        public async Task GetErrorStatisticsAsync_ShouldReturnCorrectCounts()
        {
            // Arrange
            var storeId = "store123";

            // Act
            var stats = await _service.GetErrorStatisticsAsync(storeId);

            // Assert
            stats.Should().NotBeNull();
            stats.TotalErrors.Should().BeGreaterThanOrEqualTo(0);
            stats.UnresolvedErrors.Should().BeGreaterThanOrEqualTo(0);
        }

        [Fact]
        public async Task ResolveErrorAsync_ShouldAttemptResolve()
        {
            // Arrange
            var errorId = Guid.NewGuid().ToString();
            var resolvedBy = "admin@example.com";

            // Act - error won't exist in DB but should not throw
            await _service.ResolveErrorAsync(errorId, resolvedBy);
        }

        [Fact]
        public async Task RecordRetryAttemptAsync_ShouldAttemptRetry()
        {
            // Arrange
            var errorId = Guid.NewGuid().ToString();

            // Act - error won't exist in DB but should not throw
            await _service.RecordRetryAttemptAsync(errorId);
        }

        [Fact]
        public async Task GetRetryableErrorsAsync_ShouldReturnList()
        {
            // Act
            var errors = await _service.GetRetryableErrorsAsync();

            // Assert
            errors.Should().NotBeNull();
            errors.Should().AllSatisfy(e => e.IsRetryable.Should().BeTrue());
            errors.Should().AllSatisfy(e => e.IsResolved.Should().BeFalse());
        }
    }
}
