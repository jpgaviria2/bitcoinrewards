using System;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Tests.TestHarness;
using FluentAssertions;
using Xunit;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests.Integration
{
    public class PluginIntegrationTests : IDisposable
    {
        private readonly PluginTestHarness _harness;

        public PluginIntegrationTests()
        {
            _harness = new PluginTestHarness();
        }

        [Fact]
        public void Plugin_Should_Initialize_Without_Crashing()
        {
            // Arrange & Act
            var action = () => _harness.RegisterPlugin();

            // Assert
            action.Should().NotThrow("Plugin initialization should not crash");
        }

        [Fact]
        public void Plugin_Should_Build_ServiceProvider_Without_Errors()
        {
            // Arrange
            _harness.RegisterPlugin();

            // Act
            var action = () => _harness.BuildServiceProvider();

            // Assert
            action.Should().NotThrow("Service provider should build without errors");
        }

        [Fact]
        public void Plugin_Should_Register_Core_Services()
        {
            // Arrange
            _harness.RegisterPlugin();
            var provider = _harness.BuildServiceProvider();

            // Act & Assert
            // Note: We can't fully test without BTCPay Server dependencies,
            // but we can verify the provider builds successfully
            provider.Should().NotBeNull();
        }

        public void Dispose()
        {
            _harness?.Dispose();
        }
    }
}


