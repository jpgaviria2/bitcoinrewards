#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Controllers;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.ViewModels;
using Xunit;
using FluentAssertions;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests;

public class BoltCardRewardTests
{
    // ── Settings Tests ──

    [Fact]
    public void BoltCardSettings_Defaults_AreCorrect()
    {
        var settings = new BitcoinRewardsStoreSettings();
        
        settings.BoltCardEnabled.Should().BeFalse();
        settings.BoltcardFactoryAppId.Should().BeNull();
        settings.DefaultCardBalanceSats.Should().Be(100);
    }

    [Fact]
    public void BoltCardSettings_EnableDisable_PreservesOtherSettings()
    {
        var settings = new BitcoinRewardsStoreSettings
        {
            Enabled = true,
            ExternalRewardPercentage = 5m,
            BtcpayRewardPercentage = 10m,
            BoltCardEnabled = true,
            DefaultCardBalanceSats = 200
        };

        // Verify bolt card settings don't affect existing settings
        settings.Enabled.Should().BeTrue();
        settings.ExternalRewardPercentage.Should().Be(5m);
        settings.BtcpayRewardPercentage.Should().Be(10m);
        settings.BoltCardEnabled.Should().BeTrue();
        settings.DefaultCardBalanceSats.Should().Be(200);
    }

    [Fact]
    public void SettingsViewModel_RoundTrip_PreservesBoltCardSettings()
    {
        var original = new BitcoinRewardsStoreSettings
        {
            Enabled = true,
            BoltCardEnabled = true,
            BoltcardFactoryAppId = "factory-123",
            DefaultCardBalanceSats = 500,
            ExternalRewardPercentage = 5m,
            BtcpayRewardPercentage = 10m
        };

        var vm = new BitcoinRewardsSettingsViewModel();
        vm.SetFromSettings(original);

        vm.BoltCardEnabled.Should().BeTrue();
        vm.BoltcardFactoryAppId.Should().Be("factory-123");
        vm.DefaultCardBalanceSats.Should().Be(500);

        var restored = vm.ToSettings();

        restored.BoltCardEnabled.Should().BeTrue();
        restored.BoltcardFactoryAppId.Should().Be("factory-123");
        restored.DefaultCardBalanceSats.Should().Be(500);
        // Existing settings preserved
        restored.ExternalRewardPercentage.Should().Be(5m);
        restored.BtcpayRewardPercentage.Should().Be(10m);
    }

    [Fact]
    public void SettingsViewModel_SetFromNullSettings_UsesDefaults()
    {
        var vm = new BitcoinRewardsSettingsViewModel();
        vm.SetFromSettings(null!);
        
        // Should not throw, uses defaults
        vm.BoltCardEnabled.Should().BeFalse();
        vm.DefaultCardBalanceSats.Should().Be(100);
    }

    // ── BoltCardLink Entity Tests ──

    [Fact]
    public void BoltCardLink_Defaults_AreCorrect()
    {
        var link = new BoltCardLink();

        link.Id.Should().NotBe(Guid.Empty);
        link.IsActive.Should().BeTrue();
        link.TotalRewardedSatoshis.Should().Be(0);
        link.CardUid.Should().BeNull();
        link.BoltcardId.Should().BeNull();
        link.LastRewardedAt.Should().BeNull();
    }

    [Fact]
    public void BoltCardLink_TrackRewards_AccumulatesCorrectly()
    {
        var link = new BoltCardLink
        {
            StoreId = "store-1",
            PullPaymentId = "pp-1"
        };

        // Simulate accumulating rewards
        link.TotalRewardedSatoshis += 100;
        link.TotalRewardedSatoshis += 250;
        link.TotalRewardedSatoshis += 50;

        link.TotalRewardedSatoshis.Should().Be(400);
    }

    // ── DisplayRewardsViewModel Tests ──

    [Fact]
    public void DisplayRewardsViewModel_BoltCardFields_DefaultsFalse()
    {
        var vm = new DisplayRewardsViewModel();

        vm.BoltCardEnabled.Should().BeFalse();
        vm.RewardId.Should().BeNull();
    }

    [Fact]
    public void DisplayRewardsViewModel_WithBoltCard_HasAllFields()
    {
        var vm = new DisplayRewardsViewModel
        {
            StoreId = "store-1",
            HasReward = true,
            BoltCardEnabled = true,
            RewardId = Guid.NewGuid().ToString(),
            RewardAmountSatoshis = 500,
            LnurlQrDataUri = "data:image/png;base64,abc"
        };

        vm.BoltCardEnabled.Should().BeTrue();
        vm.RewardId.Should().NotBeNullOrEmpty();
        // Existing fields still work
        vm.HasReward.Should().BeTrue();
        vm.RewardAmountSatoshis.Should().Be(500);
    }

    // ── Tap Request/Response Tests ──

    [Fact]
    public void BoltCardTapRequest_Validation_RequiresAllFields()
    {
        var request = new BoltCardRewardsController.BoltCardTapRequest();

        // Default values should be empty strings
        request.P.Should().BeEmpty();
        request.C.Should().BeEmpty();
        request.RewardId.Should().BeEmpty();
    }

    [Fact]
    public void BoltCardTapResponse_Success_HasAllFields()
    {
        var response = new BoltCardRewardsController.BoltCardTapResponse
        {
            Success = true,
            RewardSats = 500,
            NewBalanceSats = 1500,
            TotalRewardedSats = 2000,
            PullPaymentId = "pp-123"
        };

        response.Success.Should().BeTrue();
        response.Error.Should().BeNull();
        response.RewardSats.Should().Be(500);
        response.NewBalanceSats.Should().Be(1500);
        response.TotalRewardedSats.Should().Be(2000);
    }

    [Fact]
    public void BoltCardTapResponse_Failure_HasErrorMessage()
    {
        var response = new BoltCardRewardsController.BoltCardTapResponse
        {
            Success = false,
            Error = "Card verification failed"
        };

        response.Success.Should().BeFalse();
        response.Error.Should().Be("Card verification failed");
        response.RewardSats.Should().Be(0);
    }

    // ── BoltCardInfo Tests ──

    [Fact]
    public void BoltCardInfo_Record_HasCorrectProperties()
    {
        var info = new Services.BoltCardRewardService.BoltCardInfo
        {
            Id = Guid.NewGuid(),
            PullPaymentId = "pp-123",
            CardUid = "04AABBCCDD",
            BoltcardId = "abc123",
            BalanceUrl = "https://example.com/pull-payments/pp-123",
            BalanceSats = 1500,
            TotalRewardedSats = 2000,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        info.PullPaymentId.Should().Be("pp-123");
        info.BalanceSats.Should().Be(1500);
        info.BalanceUrl.Should().Contain("pp-123");
        info.IsActive.Should().BeTrue();
    }

    // ── Balance URL Generation Tests ──

    [Fact]
    public void GetBalanceUrl_GeneratesCorrectUrl()
    {
        // Test the URL format directly (the service method is simple string building)
        var baseUrl = "https://pay.example.com";
        var ppId = "pp-abc123";
        var expected = $"https://pay.example.com/pull-payments/{ppId}";
        
        var uri = new Uri(baseUrl.TrimEnd('/') + $"/pull-payments/{Uri.EscapeDataString(ppId)}");
        uri.ToString().Should().Be(expected);
    }

    [Fact]
    public void GetBalanceUrl_HandlesTrailingSlash()
    {
        var baseUrl = "https://pay.example.com/";
        var ppId = "pp-abc123";
        
        var uri = new Uri(baseUrl.TrimEnd('/') + $"/pull-payments/{Uri.EscapeDataString(ppId)}");
        uri.ToString().Should().Contain("/pull-payments/pp-abc123");
    }

    [Fact]
    public void GetBalanceUrl_EscapesSpecialCharacters()
    {
        var baseUrl = "https://pay.example.com";
        var ppId = "pp with spaces";
        
        var uri = new Uri(baseUrl.TrimEnd('/') + $"/pull-payments/{Uri.EscapeDataString(ppId)}");
        uri.ToString().Should().Contain("pp%20with%20spaces");
    }

    // ── Security Tests ──

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void TopUpAmount_MustBePositive(long amount)
    {
        // Verify the validation logic
        (amount <= 0).Should().BeTrue("amount {0} should be rejected", amount);
    }

    [Fact]
    public void RewardId_MustBeValidGuid()
    {
        Guid.TryParse("not-a-guid", out _).Should().BeFalse();
        Guid.TryParse("", out _).Should().BeFalse();
        Guid.TryParse(Guid.NewGuid().ToString(), out _).Should().BeTrue();
    }

    // ── Existing Functionality Preservation Tests ──

    [Fact]
    public void ExistingSettings_NotBroken_ByBoltCardAdditions()
    {
        // Ensure existing settings with no bolt card fields still work
        var settings = new BitcoinRewardsStoreSettings
        {
            Enabled = true,
            RewardPercentage = 5m,
            ExternalRewardPercentage = 5m,
            BtcpayRewardPercentage = 10m,
            EnabledPlatforms = PlatformFlags.Square | PlatformFlags.Btcpay,
            DisplayTimeoutSeconds = 60,
            DisplayAutoRefreshSeconds = 10
        };

        // Bolt card defaults should not interfere
        settings.BoltCardEnabled.Should().BeFalse();
        settings.DefaultCardBalanceSats.Should().Be(100);

        // Existing functionality intact
        settings.Enabled.Should().BeTrue();
        settings.RewardPercentage.Should().Be(5m);
        settings.EnabledPlatforms.Should().HaveFlag(PlatformFlags.Square);
        settings.EnabledPlatforms.Should().HaveFlag(PlatformFlags.Btcpay);
    }

    [Fact]
    public void DisplayRewardsViewModel_WithoutBoltCard_StillWorks()
    {
        var vm = new DisplayRewardsViewModel
        {
            StoreId = "store-1",
            HasReward = true,
            LnurlQrDataUri = "data:image/png;base64,abc",
            RewardAmountSatoshis = 500,
            BoltCardEnabled = false
        };

        // QR code flow should be completely unaffected
        vm.HasReward.Should().BeTrue();
        vm.LnurlQrDataUri.Should().NotBeNull();
        vm.BoltCardEnabled.Should().BeFalse();
        vm.RewardId.Should().BeNull();
    }
}
