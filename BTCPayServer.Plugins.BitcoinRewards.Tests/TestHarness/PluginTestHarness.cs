using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using BTCPayServer.Logging;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests.TestHarness
{
    /// <summary>
    /// Test harness for testing the plugin in isolation without a full BTCPay Server instance
    /// </summary>
    public class PluginTestHarness
    {
        private readonly ServiceCollection _services;
        private ServiceProvider? _serviceProvider;

        public PluginTestHarness()
        {
            _services = new ServiceCollection();
            SetupMockServices();
        }

        private void SetupMockServices()
        {
            // Add logging
            _services.AddLogging(builder => builder.AddConsole());

            // Mock BTCPay Server Logs
            var mockLogs = new Mock<Logs>();
            _services.AddSingleton(mockLogs.Object);

            // Mock ApplicationDbContext with in-memory database
            _services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}"));

            // Mock StoreRepository
            var mockStoreRepository = new Mock<StoreRepository>(
                Mock.Of<ApplicationDbContext>(),
                Mock.Of<BTCPayServer.Logging.Logs>()
            );
            _services.AddSingleton(mockStoreRepository.Object);

            // Mock RateProviderFactory
            var mockRateProviderFactory = new Mock<RateProviderFactory>();
            mockRateProviderFactory.Setup(x => x.Providers).Returns(new Dictionary<string, object>());
            _services.AddSingleton(mockRateProviderFactory.Object);

            // Mock EventAggregator (if needed)
            // Note: This might need adjustment based on actual BTCPay Server implementation
        }

        public void RegisterPlugin()
        {
            var plugin = new BTCPayServer.Plugins.BitcoinRewards.BitcoinRewardsPlugin();
            plugin.Execute(_services);
        }

        public ServiceProvider BuildServiceProvider()
        {
            if (_serviceProvider == null)
            {
                _serviceProvider = _services.BuildServiceProvider();
            }
            return _serviceProvider;
        }

        public T GetService<T>() where T : class
        {
            var provider = BuildServiceProvider();
            return provider.GetRequiredService<T>();
        }

        public void Dispose()
        {
            _serviceProvider?.Dispose();
        }
    }
}


