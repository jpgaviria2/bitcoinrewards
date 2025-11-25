#nullable enable
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using BTCPayServer.Plugins.BitcoinRewards.Clients;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers;

[AllowAnonymous]
[IgnoreAntiforgeryToken]
[Route("plugins/bitcoin-rewards/{storeId}/webhooks/square")]
public class SquareWebhookController : Controller
{
    private readonly BitcoinRewardsService _rewardsService;
    private readonly ILogger<SquareWebhookController> _logger;

    public SquareWebhookController(
        BitcoinRewardsService rewardsService,
        ILogger<SquareWebhookController> logger)
    {
        _rewardsService = rewardsService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> HandleWebhook(string storeId)
    {
        try
        {
            // Read request body
            string requestBody;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            // Get webhook signature from headers
            var signature = Request.Headers["X-Square-Signature"].ToString();
            if (string.IsNullOrEmpty(signature))
            {
                _logger.LogWarning("Square webhook missing signature");
                return BadRequest("Missing signature");
            }

            // TODO: Verify webhook signature using SquareApiClient
            // var settings = await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(storeId, ...);
            // var client = new SquareApiClient(...);
            // if (!client.VerifyWebhookSignature(requestBody, signature, settings.Square.WebhookSecret))
            //     return Unauthorized();

            // Parse webhook payload
            var jsonDoc = JsonDocument.Parse(requestBody);
            var data = jsonDoc.RootElement;

            // Handle payment.updated event
            if (data.TryGetProperty("type", out var type) && 
                type.GetString() == "payment.updated")
            {
                if (data.TryGetProperty("data", out var dataObj) &&
                    dataObj.TryGetProperty("object", out var paymentObj) &&
                    paymentObj.TryGetProperty("payment", out var payment))
                {
                    var paymentId = payment.TryGetProperty("id", out var id) ? id.GetString() : null;
                    var status = payment.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;

                    // Only process completed payments
                    if (status == "COMPLETED" && !string.IsNullOrEmpty(paymentId))
                    {
                        // Extract transaction data
                        var amountMoney = payment.TryGetProperty("amount_money", out var am) ? am : default;
                        var amount = amountMoney.TryGetProperty("amount", out var amountProp) 
                            ? amountProp.GetInt64() / 100m // Convert cents to dollars
                            : 0;
                        var currency = amountMoney.TryGetProperty("currency", out var currencyProp)
                            ? currencyProp.GetString() ?? "USD"
                            : "USD";

                        var receiptEmail = payment.TryGetProperty("receipt_email", out var emailProp) 
                            ? emailProp.GetString() 
                            : null;
                        var receiptPhone = payment.TryGetProperty("receipt_phone", out var phoneProp) 
                            ? phoneProp.GetString() 
                            : null;
                        var orderId = payment.TryGetProperty("order_id", out var orderIdProp) 
                            ? orderIdProp.GetString() 
                            : null;

                        var transaction = new TransactionData
                        {
                            TransactionId = paymentId,
                            OrderId = orderId,
                            Amount = (decimal)amount,
                            Currency = currency ?? "USD",
                            CustomerEmail = receiptEmail,
                            CustomerPhone = receiptPhone,
                            Platform = TransactionPlatform.Square,
                            TransactionDate = DateTime.UtcNow
                        };

                        await _rewardsService.ProcessRewardAsync(storeId, transaction);
                        _logger.LogInformation("Processed Square webhook for payment {PaymentId}", paymentId);
                    }
                }
            }

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Square webhook for store {StoreId}", storeId);
            return StatusCode(500);
        }
    }
}

