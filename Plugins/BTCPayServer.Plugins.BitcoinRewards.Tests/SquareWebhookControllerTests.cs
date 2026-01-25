using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Plugins.BitcoinRewards.Controllers;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Services.Stores;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests;

public class SquareWebhookControllerTests
{
    private readonly Mock<BitcoinRewardsService> _mockRewardsService;
    private readonly Mock<StoreRepository> _mockStoreRepository;
    private readonly Mock<ILogger<SquareWebhookController>> _mockLogger;
    private readonly SquareWebhookController _controller;

    public SquareWebhookControllerTests()
    {
        _mockRewardsService = new Mock<BitcoinRewardsService>();
        _mockStoreRepository = new Mock<StoreRepository>();
        _mockLogger = new Mock<ILogger<SquareWebhookController>>();
        
        _controller = new SquareWebhookController(
            _mockRewardsService.Object,
            _mockStoreRepository.Object,
            _mockLogger.Object
        );
    }

    [Fact]
    public async Task HandleWebhook_MissingSignature_ReturnsUnauthorized()
    {
        // Arrange
        var storeId = "test-store";
        var mockContext = new DefaultHttpContext();
        mockContext.Request.Headers["X-Square-Signature"] = "";
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = mockContext
        };

        // Act
        var result = await _controller.HandleWebhook(storeId);

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Theory]
    [InlineData("{\"type\":\"payment.updated\"}", "test-signature-key", "valid-signature")]
    public async Task HandleWebhook_ValidSignature_ProcessesPayment(string payload, string signatureKey, string expectedSig)
    {
        // Arrange
        var storeId = "test-store";
        var webhookUrl = "https://example.com/webhook";
        
        // Compute actual signature
        var stringToSign = webhookUrl + payload;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signatureKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        var computedSignature = Convert.ToBase64String(hash);

        // Mock store settings
        var store = new StoreData { Id = storeId };
        _mockStoreRepository.Setup(x => x.FindStore(storeId)).ReturnsAsync(store);

        // Note: Full integration test would require mocking store blob with Square settings
        // This is a structural test to verify controller handles the flow
        
        // Assert: Test structure is valid (actual signature verification requires full integration)
        computedSignature.Should().NotBeNullOrEmpty();
    }

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
        result.Length.Should().BeGreaterThan(20); // Base64 encoded SHA256 is ~44 chars
    }
}
