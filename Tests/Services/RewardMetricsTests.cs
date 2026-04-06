using System;
using System.Linq;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using FluentAssertions;
using Xunit;

namespace BitcoinRewards.Tests.Services
{
    public class RewardMetricsTests
    {
        private readonly RewardMetrics _metrics;

        public RewardMetricsTests()
        {
            _metrics = new RewardMetrics();
        }

        [Fact]
        public void RecordRewardCreated_ShouldIncrementCounter()
        {
            // Arrange
            var platform = "square";
            var storeId = "store123";

            // Act
            _metrics.RecordRewardCreated(platform, storeId);
            _metrics.RecordRewardCreated(platform, storeId);

            // Assert
            var snapshot = _metrics.GetSnapshot();
            var key = $"rewards_created_total|platform={platform}|store={storeId}";
            snapshot.Counters.Should().ContainKey(key);
            snapshot.Counters[key].Should().Be(2);
        }

        [Fact]
        public void RecordRewardClaimed_ShouldIncrementCounter()
        {
            // Arrange
            var method = "lnurl";
            var storeId = "store123";

            // Act
            _metrics.RecordRewardClaimed(method, storeId, 1000m);

            // Assert
            var snapshot = _metrics.GetSnapshot();
            var key = $"rewards_claimed_total|method={method}|store={storeId}";
            snapshot.Counters.Should().ContainKey(key);
            snapshot.Counters[key].Should().Be(1);
        }

        [Fact]
        public void RecordRewardAmount_ShouldAddToHistogram()
        {
            // Arrange
            var amounts = new[] { 1000L, 2000L, 3000L, 4000L, 5000L };

            // Act
            foreach (var amount in amounts)
            {
                _metrics.RecordRewardAmount(amount);
            }

            // Assert
            var snapshot = _metrics.GetSnapshot();
            snapshot.Histograms.Should().ContainKey("reward_amount_satoshis");
            var histogram = snapshot.Histograms["reward_amount_satoshis"];
            histogram.Count.Should().Be(5);
            histogram.Sum.Should().Be(15000);
            histogram.Mean.Should().Be(3000);
            histogram.Min.Should().Be(1000);
            histogram.Max.Should().Be(5000);
        }

        [Fact]
        public void RecordError_ShouldIncrementErrorCounter()
        {
            // Arrange
            var errorType = "webhook";
            var storeId = "store123";
            var reason = "signature_invalid";

            // Act
            _metrics.RecordError(errorType, storeId, reason);
            _metrics.RecordError(errorType, storeId, reason);

            // Assert
            var snapshot = _metrics.GetSnapshot();
            var key = $"errors_total|error_type={errorType}|store={storeId}";
            snapshot.Counters.Should().ContainKey(key);
            snapshot.Counters[key].Should().Be(2);
        }

        [Fact]
        public void RecordWebhookReceived_ShouldIncrementCounter()
        {
            // Arrange
            var platform = "square";
            var storeId = "store123";

            // Act
            _metrics.RecordWebhookReceived(platform, storeId);

            // Assert
            var snapshot = _metrics.GetSnapshot();
            var key = $"webhooks_received_total|platform={platform}|store={storeId}";
            snapshot.Counters.Should().ContainKey(key);
            snapshot.Counters[key].Should().Be(1);
        }

        [Fact]
        public void RecordWebhookDuration_ShouldAddToHistogram()
        {
            // Arrange
            var platform = "square";
            var storeId = "store123";
            var durations = new[] { 100.0, 200.0, 300.0, 400.0, 500.0 };

            // Act
            foreach (var duration in durations)
            {
                _metrics.RecordWebhookDuration(platform, storeId, duration, true);
            }

            // Assert
            var snapshot = _metrics.GetSnapshot();
            var key = $"webhook_duration_ms|platform={platform}|store={storeId}";
            snapshot.Histograms.Should().ContainKey(key);
            var histogram = snapshot.Histograms[key];
            histogram.Count.Should().Be(5);
            histogram.Mean.Should().Be(300);
        }

        [Fact]
        public void RecordOperationDuration_ShouldCalculatePercentiles()
        {
            // Arrange
            var durations = Enumerable.Range(1, 100).Select(i => (double)i).ToArray();

            // Act
            foreach (var duration in durations)
            {
                _metrics.RecordOperationDuration("claim", duration);
            }

            // Assert
            var snapshot = _metrics.GetSnapshot();
            snapshot.Histograms.Should().ContainKey("operation_duration_ms|operation=claim");
            var histogram = snapshot.Histograms["operation_duration_ms|operation=claim"];
            histogram.P50.Should().BeApproximately(50, 5);
            histogram.P95.Should().BeApproximately(95, 5);
            histogram.P99.Should().BeApproximately(99, 2);
        }

        [Fact]
        public void UpdateActiveRewards_ShouldSetGauge()
        {
            // Arrange
            var storeId = "store123";
            var count = 42;

            // Act
            _metrics.UpdateActiveRewards(storeId, count);

            // Assert
            var snapshot = _metrics.GetSnapshot();
            var key = $"active_rewards|store={storeId}";
            snapshot.Gauges.Should().ContainKey(key);
            snapshot.Gauges[key].Should().Be(count);
        }

        [Fact]
        public void UpdateUnclaimedValue_ShouldSetGauge()
        {
            // Arrange
            var storeId = "store123";
            var value = 1000000L;

            // Act
            _metrics.UpdateUnclaimedValue(storeId, value);

            // Assert
            var snapshot = _metrics.GetSnapshot();
            var key = $"unclaimed_rewards_sats|store={storeId}";
            snapshot.Gauges.Should().ContainKey(key);
            snapshot.Gauges[key].Should().Be(value);
        }

        [Fact]
        public void ExportPrometheusFormat_ShouldGenerateValidOutput()
        {
            // Arrange
            _metrics.RecordRewardCreated("square", "store123");
            _metrics.RecordRewardAmount(5000);
            _metrics.UpdateActiveRewards("store123", 10);

            // Act
            var output = _metrics.ExportPrometheusFormat();

            // Assert
            output.Should().NotBeNullOrEmpty();
            output.Should().Contain("rewards_created_total");
            output.Should().Contain("reward_amount_satoshis");
            output.Should().Contain("active_rewards");
            output.Should().Contain("platform=square");
            output.Should().Contain("store=store123");
        }

        [Fact]
        public void GetSnapshot_ShouldReturnConsistentData()
        {
            // Arrange
            _metrics.RecordRewardCreated("square", "store123");
            _metrics.RecordRewardAmount(1000);
            _metrics.UpdateActiveRewards("store123", 5);

            // Act
            var snapshot1 = _metrics.GetSnapshot();
            var snapshot2 = _metrics.GetSnapshot();

            // Assert
            snapshot1.Counters.Should().BeEquivalentTo(snapshot2.Counters);
            snapshot1.Gauges.Should().BeEquivalentTo(snapshot2.Gauges);
            snapshot1.Histograms.Keys.Should().BeEquivalentTo(snapshot2.Histograms.Keys);
        }

        [Fact]
        public void Histogram_ShouldRespectMaxObservations()
        {
            // Arrange
            var maxObservations = 1000;

            // Act - Add more than max observations
            for (int i = 0; i < maxObservations + 500; i++)
            {
                _metrics.RecordRewardAmount(i);
            }

            // Assert
            var snapshot = _metrics.GetSnapshot();
            var histogram = snapshot.Histograms["reward_amount_satoshis"];
            histogram.Count.Should().BeLessOrEqualTo(maxObservations);
        }

        [Fact]
        public void RecordLightningOperation_ShouldTrackSuccessAndFailure()
        {
            // Arrange
            var operation = "pull_payment_created";
            var storeId = "store123";

            // Act
            _metrics.RecordLightningOperation(operation, storeId, true);
            _metrics.RecordLightningOperation(operation, storeId, true);
            _metrics.RecordLightningOperation(operation, storeId, false);

            // Assert
            var snapshot = _metrics.GetSnapshot();
            var successKey = $"lightning_operations_total|operation={operation}|store={storeId}|success=True";
            var failureKey = $"lightning_operations_total|operation={operation}|store={storeId}|success=False";

            snapshot.Counters[successKey].Should().Be(2);
            snapshot.Counters[failureKey].Should().Be(1);
        }
    }
}
