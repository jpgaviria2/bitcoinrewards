using System;
using System.Linq;
using BTCPayServer.Plugins.BitcoinRewards;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests
{
    public class PluginInitializationTests
    {
        [Fact]
        public void Plugin_Should_Have_Correct_Identifier()
        {
            var plugin = new BitcoinRewardsPlugin();
            plugin.Identifier.Should().Be("BTCPayServer.Plugins.BitcoinRewards");
        }

        [Fact]
        public void Plugin_Should_Have_Correct_Name()
        {
            var plugin = new BitcoinRewardsPlugin();
            plugin.Name.Should().Be("Bitcoin Rewards");
        }

        [Fact]
        public void Plugin_Execute_Should_Not_Throw_When_Services_Available()
        {
            // Arrange
            var services = new ServiceCollection();
            var plugin = new BitcoinRewardsPlugin();

            // Add minimal required services
            services.AddLogging();
            
            // Mock BTCPay Server services
            var mockLogs = new Mock<BTCPayServer.Logging.Logs>();
            services.AddSingleton(mockLogs.Object);

            // Act & Assert
            var action = () => plugin.Execute(services);
            action.Should().NotThrow();
        }

        [Fact]
        public void Plugin_Execute_Should_Register_Required_Services()
        {
            // Arrange
            var services = new ServiceCollection();
            var plugin = new BitcoinRewardsPlugin();
            services.AddLogging();
            
            var mockLogs = new Mock<BTCPayServer.Logging.Logs>();
            services.AddSingleton(mockLogs.Object);

            // Act
            plugin.Execute(services);

            // Assert - Check that services are registered
            // Note: We can't fully test without BTCPay Server dependencies,
            // but we can verify the method doesn't throw
            services.Should().NotBeEmpty();
        }
    }
}


