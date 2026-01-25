#nullable enable
#if DEBUG
using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("plugins/bitcoin-rewards/{storeId}/test")]
public class TestRewardsController : Controller
{
    private readonly BitcoinRewardsService _service;
    private readonly ILogger<TestRewardsController> _logger;

    public TestRewardsController(
        BitcoinRewardsService service,
        ILogger<TestRewardsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Test Square reward processing with specified amount and currency
    /// POST /plugins/bitcoin-rewards/{storeId}/test/square?amount=10&currency=CAD
    /// </summary>
    [HttpPost("square")]
    public async Task<IActionResult> TestSquareReward(
        string storeId, 
        [FromQuery] decimal amount = 10.00m, 
        [FromQuery] string currency = "CAD",
        [FromQuery] string? email = null)
    {
        try
        {
            _logger.LogInformation("🧪 TEST SQUARE REWARD: Store={StoreId}, Amount={Amount} {Currency}", 
                storeId, amount, currency);

            var transaction = new TransactionData
            {
                TransactionId = $"TEST_SQUARE_{Guid.NewGuid():N}",
                OrderId = $"TEST_ORDER_{DateTime.UtcNow:yyyyMMddHHmmss}",
                Amount = amount,
                Currency = currency,
                CustomerEmail = email,
                Platform = TransactionPlatform.Square,
                TransactionDate = DateTime.UtcNow
            };

            var result = await _service.ProcessRewardAsync(storeId, transaction);

            return Ok(new
            {
                success = result,
                transactionId = transaction.TransactionId,
                orderId = transaction.OrderId,
                amount = transaction.Amount,
                currency = transaction.Currency,
                message = result 
                    ? "✅ Test reward processed successfully!" 
                    : "❌ Reward processing failed - check logs for details",
                instructions = "Run: docker logs -f generated_btcpayserver_1 | grep '[RATE FETCH]'"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🧪 TEST FAILED: Error processing test Square reward");
            return StatusCode(500, new
            {
                success = false,
                error = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }

    /// <summary>
    /// Simple ping endpoint to verify the test API is accessible
    /// GET /plugins/bitcoin-rewards/{storeId}/test/ping
    /// </summary>
    [HttpGet("ping")]
    public IActionResult Ping(string storeId)
    {
        return Ok(new
        {
            message = "Bitcoin Rewards Test API is working!",
            storeId,
            timestamp = DateTime.UtcNow,
            testEndpoint = $"/plugins/bitcoin-rewards/{storeId}/test/square",
            examples = new
            {
                basic = $"POST /plugins/bitcoin-rewards/{storeId}/test/square",
                withParams = $"POST /plugins/bitcoin-rewards/{storeId}/test/square?amount=15.99&currency=CAD&email=test@example.com"
            }
        });
    }
}
#endif
