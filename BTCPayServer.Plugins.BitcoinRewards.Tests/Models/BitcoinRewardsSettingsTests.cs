using BTCPayServer.Plugins.BitcoinRewards.Models;
using FluentAssertions;
using Xunit;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests.Models
{
    public class BitcoinRewardsSettingsTests
    {
        [Fact]
        public void CredentialsPopulated_When_WebhookSecret_Is_Set_Should_Return_True()
        {
            // Arrange
            var settings = new BitcoinRewardsSettings
            {
                WebhookSecret = "test-secret"
            };

            // Act
            var result = settings.CredentialsPopulated();

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void CredentialsPopulated_When_WebhookSecret_Is_Empty_Should_Return_False()
        {
            // Arrange
            var settings = new BitcoinRewardsSettings
            {
                WebhookSecret = null
            };

            // Act
            var result = settings.CredentialsPopulated();

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void SquareCredentialsPopulated_When_All_Fields_Set_Should_Return_True()
        {
            // Arrange
            var settings = new BitcoinRewardsSettings
            {
                SquareApplicationId = "app-id",
                SquareAccessToken = "token",
                SquareLocationId = "location-id"
            };

            // Act
            var result = settings.SquareCredentialsPopulated();

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public void SquareCredentialsPopulated_When_Any_Field_Missing_Should_Return_False()
        {
            // Arrange
            var settings = new BitcoinRewardsSettings
            {
                SquareApplicationId = "app-id",
                SquareAccessToken = null,
                SquareLocationId = "location-id"
            };

            // Act
            var result = settings.SquareCredentialsPopulated();

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void Default_Values_Should_Be_Set_Correctly()
        {
            // Arrange & Act
            var settings = new BitcoinRewardsSettings();

            // Assert
            settings.RewardPercentage.Should().Be(0.01m);
            settings.MinimumOrderAmount.Should().Be(0m);
            settings.MaximumRewardAmount.Should().Be(1000m);
            settings.WalletPreference.Should().Be(WalletPreference.LightningFirst);
            settings.SquareEnvironment.Should().Be("production");
            settings.PreferredExchangeRateProvider.Should().Be("coingecko");
        }
    }
}


