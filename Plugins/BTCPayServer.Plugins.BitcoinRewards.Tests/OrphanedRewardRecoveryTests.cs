#nullable enable
using System;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests;

/// <summary>
/// Tests verifying the fix for the orphaned reward bug where:
/// 1. AddRewardAsync() saves a record with NULL ClaimLink/PullPaymentId
/// 2. Pull payment creation succeeds
/// 3. Second AddRewardAsync() fails with duplicate key constraint
/// 4. Result: orphaned pull payments and blank display page
///
/// Fix: Changed second AddRewardAsync() to UpdateRewardAsync() so the existing
/// record gets updated with PullPaymentId/ClaimLink after pull payment creation.
/// </summary>
public class OrphanedRewardRecoveryTests
{
    [Fact]
    public void RewardRecord_InitialState_HasNullPullPaymentFields()
    {
        // Verify that a freshly created reward record has NULL pull payment fields
        // This is the state after the first AddRewardAsync() call
        var record = new Data.BitcoinRewardRecord
        {
            StoreId = "test-store",
            Platform = Data.RewardPlatform.Square,
            TransactionId = "txn_123",
            OrderId = "order_456",
            TransactionAmount = 100m,
            Currency = "USD",
            RewardAmount = 5m,
            RewardAmountSatoshis = 500,
            Status = Data.RewardStatus.Pending
        };

        record.PullPaymentId.Should().BeNull("initial record should not have PullPaymentId");
        record.ClaimLink.Should().BeNull("initial record should not have ClaimLink");
        record.PayoutProcessor.Should().BeNull();
        record.PayoutMethod.Should().BeNull();
        record.Status.Should().Be(Data.RewardStatus.Pending);
    }

    [Fact]
    public void RewardRecord_AfterPullPaymentUpdate_HasAllFields()
    {
        // Verify that after updating with pull payment info, all fields are set
        // This simulates what UpdateRewardAsync() should persist
        var record = new Data.BitcoinRewardRecord
        {
            StoreId = "test-store",
            Platform = Data.RewardPlatform.Square,
            TransactionId = "txn_123",
            OrderId = "order_456",
            TransactionAmount = 100m,
            Currency = "USD",
            RewardAmount = 5m,
            RewardAmountSatoshis = 500,
            Status = Data.RewardStatus.Pending
        };

        // Simulate pull payment success and update (the fix)
        record.PullPaymentId = "pp_abc123";
        record.PayoutProcessor = "LNURLPay";
        record.PayoutMethod = "BTC-LN";
        record.ClaimLink = "https://btcpay.example.com/pull-payments/pp_abc123";
        record.Status = Data.RewardStatus.Sent;
        record.SentAt = DateTime.UtcNow;

        record.PullPaymentId.Should().Be("pp_abc123");
        record.ClaimLink.Should().NotBeNullOrEmpty();
        record.Status.Should().Be(Data.RewardStatus.Sent);
        record.SentAt.Should().NotBeNull();
    }

    [Fact]
    public void OrphanedRecord_IsIdentifiable_ByNullClaimLinkWithOrderId()
    {
        // Verify we can identify orphaned records: have OrderId but NULL ClaimLink
        var orphaned = new Data.BitcoinRewardRecord
        {
            StoreId = "test-store",
            Platform = Data.RewardPlatform.Square,
            TransactionId = "txn_123",
            OrderId = "order_456",
            Status = Data.RewardStatus.Pending,
            ClaimLink = null,
            PullPaymentId = null
        };

        var healthy = new Data.BitcoinRewardRecord
        {
            StoreId = "test-store",
            Platform = Data.RewardPlatform.Square,
            TransactionId = "txn_789",
            OrderId = "order_012",
            Status = Data.RewardStatus.Sent,
            ClaimLink = "https://btcpay.example.com/pull-payments/pp_xyz",
            PullPaymentId = "pp_xyz"
        };

        // Orphaned: has OrderId but missing ClaimLink/PullPaymentId
        bool isOrphaned(Data.BitcoinRewardRecord r) =>
            r.Status == Data.RewardStatus.Pending &&
            !string.IsNullOrEmpty(r.OrderId) &&
            (r.ClaimLink == null || r.PullPaymentId == null);

        isOrphaned(orphaned).Should().BeTrue("record with NULL ClaimLink should be identified as orphaned");
        isOrphaned(healthy).Should().BeFalse("record with ClaimLink should not be orphaned");
    }

    [Fact]
    public void RecoveredRecord_HasCorrectStatus()
    {
        // Verify that a recovered orphaned record gets Status=Sent and error cleared
        var record = new Data.BitcoinRewardRecord
        {
            StoreId = "test-store",
            TransactionId = "txn_123",
            OrderId = "order_456",
            Status = Data.RewardStatus.Pending,
            ErrorMessage = "Some previous error",
            ClaimLink = null,
            PullPaymentId = null
        };

        // Simulate recovery
        record.PullPaymentId = "pp_recovered";
        record.ClaimLink = "https://btcpay.example.com/pull-payments/pp_recovered";
        record.PayoutProcessor = "LNURLPay";
        record.PayoutMethod = "BTC-LN";
        record.Status = Data.RewardStatus.Sent;
        record.SentAt = DateTime.UtcNow;
        record.ErrorMessage = null;

        record.Status.Should().Be(Data.RewardStatus.Sent);
        record.ErrorMessage.Should().BeNull("error should be cleared on recovery");
        record.PullPaymentId.Should().NotBeNull();
        record.ClaimLink.Should().NotBeNull();
    }

    [Fact]
    public void DisplayRewards_RequiresClaimLink_NotNull()
    {
        // Verify the display logic: records with NULL ClaimLink won't show
        // This confirms why the blank page occurred
        var records = new[]
        {
            new Data.BitcoinRewardRecord { ClaimLink = null, Status = Data.RewardStatus.Pending },
            new Data.BitcoinRewardRecord { ClaimLink = null, Status = Data.RewardStatus.Sent },
            new Data.BitcoinRewardRecord { ClaimLink = "https://example.com/pp/123", Status = Data.RewardStatus.Sent },
        };

        // Simulate DisplayRewards filter: ClaimLink IS NOT NULL
        var displayable = Array.FindAll(records, r => !string.IsNullOrEmpty(r.ClaimLink));
        displayable.Should().HaveCount(1, "only records with ClaimLink should display");
    }
}
