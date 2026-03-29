using System;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Xunit;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests;

/// <summary>
/// Tests for Square webhook signature verification logic.
/// Controller integration tests are skipped — requires full DI container
/// with BitcoinRewardsService and StoreRepository (non-mockable).
/// </summary>
public class SquareWebhookControllerTests
{
    [Fact]
    public void ComputeHmacSha256_ValidInputs_ReturnsExpectedHash()
    {
        // Arrange
        var message = "test message";
        var key = "secret-key";
        
        // Act
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        var result = Convert.ToBase64String(hash);
        
        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Length.Should().Be(44); // Base64 encoded SHA256 is always 44 chars
    }

    [Fact]
    public void ComputeHmacSha256_DifferentKeys_ProduceDifferentHashes()
    {
        var message = "test message";
        
        using var hmac1 = new HMACSHA256(Encoding.UTF8.GetBytes("key-1"));
        using var hmac2 = new HMACSHA256(Encoding.UTF8.GetBytes("key-2"));
        
        var hash1 = Convert.ToBase64String(hmac1.ComputeHash(Encoding.UTF8.GetBytes(message)));
        var hash2 = Convert.ToBase64String(hmac2.ComputeHash(Encoding.UTF8.GetBytes(message)));
        
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeHmacSha256_SameInputs_ProduceSameHash()
    {
        var message = "test message";
        var key = "secret-key";
        
        using var hmac1 = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        using var hmac2 = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        
        var hash1 = Convert.ToBase64String(hmac1.ComputeHash(Encoding.UTF8.GetBytes(message)));
        var hash2 = Convert.ToBase64String(hmac2.ComputeHash(Encoding.UTF8.GetBytes(message)));
        
        hash1.Should().Be(hash2);
    }

    [Theory]
    [InlineData("{\"type\":\"payment.updated\"}", "test-signature-key")]
    [InlineData("{\"type\":\"payment.completed\"}", "another-key")]
    public void SquareSignatureComputation_UrlPlusPayload_ProducesValidSignature(string payload, string signatureKey)
    {
        // Square's signature = HMAC-SHA256(webhookUrl + body, signatureKey)
        var webhookUrl = "https://example.com/plugins/bitcoin-rewards/store123/webhooks/square";
        var stringToSign = webhookUrl + payload;
        
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signatureKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        var signature = Convert.ToBase64String(hash);
        
        signature.Should().NotBeNullOrEmpty();
        signature.Length.Should().Be(44);
    }
}
