using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Logging;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using FluentAssertions;
using Moq;
using Xunit;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests.Services
{
    public class RateServiceTests
    {
        private readonly Mock<RateProviderFactory> _mockRateProviderFactory;
        private readonly Mock<Logs> _mockLogs;
        private readonly RateService _rateService;

        public RateServiceTests()
        {
            _mockRateProviderFactory = new Mock<RateProviderFactory>();
            _mockLogs = new Mock<Logs>();
            
            // Setup default providers dictionary
            var providers = new Dictionary<string, object>();
            _mockRateProviderFactory.Setup(x => x.Providers).Returns(providers);
            
            _rateService = new RateService(_mockRateProviderFactory.Object, _mockLogs.Object);
        }

        [Fact]
        public async Task ConvertToBTC_When_Currency_Is_BTC_Should_Return_Same_Amount()
        {
            // Arrange
            var amount = 0.001m;
            var currency = "BTC";

            // Act
            var result = await _rateService.ConvertToBTC(amount, currency);

            // Assert
            result.Should().Be(amount);
        }

        [Fact]
        public async Task ConvertToBTC_When_Provider_Not_Available_Should_Use_Fallback()
        {
            // Arrange
            var amount = 100m;
            var currency = "USD";
            
            // Provider not in dictionary, should use fallback
            _mockRateProviderFactory.Object.Providers.Clear();

            // Act
            var result = await _rateService.ConvertToBTC(amount, currency);

            // Assert
            // Fallback rate is 50000 USD/BTC, so 100 USD = 0.002 BTC
            result.Should().BeApproximately(0.002m, 0.0001m);
        }

        [Fact]
        public async Task ConvertToBTC_Should_Handle_Null_Provider_Gracefully()
        {
            // Arrange
            var amount = 100m;
            var currency = "USD";
            string? provider = null;

            // Act
            var result = await _rateService.ConvertToBTC(amount, currency, provider);

            // Assert
            result.Should().BeGreaterThan(0);
        }
    }
}


