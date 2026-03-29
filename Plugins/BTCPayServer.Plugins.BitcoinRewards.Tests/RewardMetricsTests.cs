using System;
using BTCPayServer.Plugins.BitcoinRewards.Exceptions;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using Xunit;
using FluentAssertions;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests;

public class RewardMetricsTests
{
    private readonly RewardMetrics _metrics;

    public RewardMetricsTests()
    {
        _metrics = new RewardMetrics();
    }

    [Fact]
    public void RecordRewardCreated_IncrementsCounter()
    {
        // Act
        _metrics.RecordRewardCreated("square", "store-1");
        _metrics.RecordRewardCreated("square", "store-1");
        _metrics.RecordRewardCreated("shopify", "store-2");

        // Assert
        _metrics.GetCounter("rewards_created_total").Should().BeGreaterOrEqualTo(3);
    }

    [Fact]
    public void RecordRewardCreated_WithAmount_RecordsObservation()
    {
        // Act
        _metrics.RecordRewardCreated(1000m, "square", "store-1");
        _metrics.RecordRewardCreated(2000m, "square", "store-1");

        // Assert
        _metrics.GetCounter("rewards_created_total").Should().BeGreaterOrEqualTo(2);
        var stats = _metrics.GetHistogramStats("reward_amount_satoshis");
        stats.Should().NotBeNull();
        stats!.Count.Should().Be(2);
    }

    [Fact]
    public void RecordRewardAmount_TracksHistogram()
    {
        // Act
        _metrics.RecordRewardAmount(100);
        _metrics.RecordRewardAmount(200);
        _metrics.RecordRewardAmount(300);

        // Assert
        var stats = _metrics.GetHistogramStats("reward_amount_satoshis");
        stats.Should().NotBeNull();
        stats!.Count.Should().Be(3);
        stats.Min.Should().Be(100);
        stats.Max.Should().Be(300);
        stats.Mean.Should().Be(200);
    }

    [Fact]
    public void RecordRewardClaimed_IncrementsCounter()
    {
        // Act
        _metrics.RecordRewardClaimed("email", "store-1", 500m);

        // Assert: GetCounter uses StartsWith, so it sums all dimensional sub-keys
        // RecordRewardClaimed creates 3 keys per call (method+store, method, total)
        _metrics.GetCounter("rewards_claimed_total").Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void RecordError_SimpleOverload_IncrementsCounter()
    {
        // Act
        _metrics.RecordError("webhook", "store-1", "invalid_signature");
        _metrics.RecordError("webhook", "store-1", "timeout");

        // Assert: GetCounter sums all dimensional sub-keys (3 per call)
        _metrics.GetCounter("errors_total").Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public void RecordError_EnumOverload_IncrementsCounter()
    {
        // Act
        _metrics.RecordError(RewardErrorType.SquareApiError, "store-1");

        // Assert: GetCounter sums all dimensional sub-keys (3 per call)
        _metrics.GetCounter("reward_errors_total").Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void RecordWebhookReceived_IncrementsCounter()
    {
        // Act
        _metrics.RecordWebhookReceived("square", "store-1");
        _metrics.RecordWebhookReceived("square", "store-1");

        // Assert
        _metrics.GetCounter("webhooks_received_total").Should().Be(2);
    }

    [Fact]
    public void UpdateActiveRewards_SetsGauge()
    {
        // Act
        _metrics.UpdateActiveRewards("store-1", 42);

        // Assert
        _metrics.GetGauge("active_rewards").Should().Be(42);
    }

    [Fact]
    public void UpdateActiveRewards_OverwritesPreviousValue()
    {
        // Act
        _metrics.UpdateActiveRewards("store-1", 10);
        _metrics.UpdateActiveRewards("store-1", 5);

        // Assert
        _metrics.GetGauge("active_rewards").Should().Be(5);
    }

    [Fact]
    public void UpdateUnclaimedValue_SetsGauge()
    {
        // Act
        _metrics.UpdateUnclaimedValue("store-1", 100000m);

        // Assert
        _metrics.GetGauge("unclaimed_rewards_sats").Should().Be(100000);
    }

    [Fact]
    public void RecordOperationDuration_TracksHistogram()
    {
        // Act
        _metrics.RecordOperationDuration("process_reward", 50.0);
        _metrics.RecordOperationDuration("process_reward", 100.0);
        _metrics.RecordOperationDuration("process_reward", 150.0);

        // Assert
        var stats = _metrics.GetHistogramStats("operation_duration_ms");
        stats.Should().NotBeNull();
        stats!.Count.Should().Be(3);
        stats.Min.Should().Be(50.0);
        stats.Max.Should().Be(150.0);
    }

    [Fact]
    public void RecordWebhookDuration_TracksHistogramAndCounter()
    {
        // Act
        _metrics.RecordWebhookDuration("square", "store-1", 250.0, true);

        // Assert
        var stats = _metrics.GetHistogramStats("webhook_duration_ms");
        stats.Should().NotBeNull();
        stats!.Count.Should().Be(1);
        stats.Mean.Should().Be(250.0);

        _metrics.GetCounter("webhooks_processed_total").Should().Be(1);
    }

    [Fact]
    public void GetHistogramStats_ReturnsNull_ForNonexistentHistogram()
    {
        // Act
        var stats = _metrics.GetHistogramStats("nonexistent_metric");

        // Assert
        stats.Should().BeNull();
    }

    [Fact]
    public void GetHistogramStats_CalculatesPercentiles()
    {
        // Arrange: add 100 observations (1-100)
        for (int i = 1; i <= 100; i++)
        {
            _metrics.RecordOperationDuration("percentile_test", i);
        }

        // Act
        var stats = _metrics.GetHistogramStats("operation_duration_ms|operation=percentile_test");

        // Assert
        stats.Should().NotBeNull();
        stats!.Count.Should().Be(100);
        stats.P50.Should().Be(50);
        stats.P95.Should().Be(95);
        stats.P99.Should().Be(99);
    }

    [Fact]
    public void GetSummary_ReturnsAllMetrics()
    {
        // Arrange
        _metrics.RecordRewardCreated("square", "store-1");
        _metrics.UpdateActiveRewards("store-1", 10);
        _metrics.RecordOperationDuration("test", 100);

        // Act
        var summary = _metrics.GetSummary();

        // Assert
        summary.Should().NotBeNull();
        summary.Counters.Should().NotBeEmpty();
        summary.Gauges.Should().NotBeEmpty();
        summary.Histograms.Should().NotBeEmpty();
    }

    [Fact]
    public void GetSnapshot_ReturnsSameAsSummary()
    {
        // Arrange
        _metrics.RecordRewardCreated("square", "store-1");

        // Act
        var snapshot = _metrics.GetSnapshot();
        var summary = _metrics.GetSummary();

        // Assert
        snapshot.Should().NotBeNull();
        snapshot.Counters.Count.Should().Be(summary.Counters.Count);
    }

    [Fact]
    public void ExportPrometheusFormat_ReturnsNonEmptyString()
    {
        // Arrange
        _metrics.RecordRewardCreated("square", "store-1");
        _metrics.UpdateActiveRewards("store-1", 5);

        // Act
        var output = _metrics.ExportPrometheusFormat();

        // Assert
        output.Should().NotBeNullOrEmpty();
        output.Should().Contain("rewards_created_total");
        output.Should().Contain("active_rewards");
    }

    [Fact]
    public void RecordLightningOperation_IncrementsCounter()
    {
        // Act
        _metrics.RecordLightningOperation("pay_invoice", "store-1", true);
        _metrics.RecordLightningOperation("pay_invoice", "store-1", false);

        // Assert
        _metrics.GetCounter("lightning_operations_total").Should().Be(2);
    }
}
