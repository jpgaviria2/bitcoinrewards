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
using BTCPayServer.Services.Stores;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers;

[AllowAnonymous]
[IgnoreAntiforgeryToken]
[Route("plugins/bitcoin-rewards/{storeId}/webhooks/square")]
public class SquareWebhookController : Controller
{
    private readonly BitcoinRewardsService _rewardsService;
    private readonly StoreRepository _storeRepository;
    private readonly ILogger<SquareWebhookController> _logger;

    public SquareWebhookController(
        BitcoinRewardsService rewardsService,
        StoreRepository storeRepository,
        ILogger<SquareWebhookController> logger)
    {
        _rewardsService = rewardsService;
        _storeRepository = storeRepository;
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
            var signature = Request.Headers["X-Square-Signature"].ToString()?.Trim();
            if (string.IsNullOrEmpty(signature))
            {
                _logger.LogWarning("Square webhook missing signature");
                return BadRequest("Missing signature");
            }

            var settings = await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(
                storeId,
                BitcoinRewardsStoreSettings.SettingsName);
            var signatureKey = settings?.Square?.WebhookSignatureKey?.Trim();
            var primaryUrl = Request.GetEncodedUrl();
            var candidateUrls = BuildSignatureUrls(primaryUrl);

            if (string.IsNullOrWhiteSpace(signatureKey))
            {
                _logger.LogWarning("Square webhook signature key not configured for store {StoreId}", storeId);
                return Unauthorized();
            }

            var verified = false;
            string? firstComputedSha1 = null;
            string? firstComputedSha256 = null;
            foreach (var url in candidateUrls)
            {
                var computedSha1 = ComputeSignatureSha1(url, requestBody, signatureKey);
                var computedSha256 = ComputeSignatureSha256(url, requestBody, signatureKey);
                firstComputedSha1 ??= computedSha1;
                firstComputedSha256 ??= computedSha256;

                if (!string.IsNullOrEmpty(computedSha1) &&
                    CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(computedSha1),
                        Encoding.UTF8.GetBytes(signature)))
                {
                    verified = true;
                    break;
                }

                if (!string.IsNullOrEmpty(computedSha256) &&
                    CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(computedSha256),
                        Encoding.UTF8.GetBytes(signature)))
                {
                    verified = true;
                    break;
                }
            }

            if (!verified)
            {
                _logger.LogWarning("Square webhook signature verification failed for store {StoreId} (sig={Sig}, urls tried: {Urls}, computedSha1={Sha1}, computedSha256={Sha256})",
                    storeId, signature, string.Join(", ", candidateUrls), firstComputedSha1 ?? "n/a", firstComputedSha256 ?? "n/a");
                return Unauthorized();
            }

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
                        // Square payload in our flow already comes in major units; avoid dividing by 100
                        var amount = amountMoney.TryGetProperty("amount", out var amountProp) 
                            ? amountProp.GetDecimal()
                            : 0;
                        var currency = amountMoney.TryGetProperty("currency", out var currencyProp)
                            ? currencyProp.GetString() ?? "USD"
                            : "USD";

                        // Prefer receipt_email; fall back to buyer_email_address (Square sends this instead in many cases)
                        string? receiptEmail = null;
                        if (payment.TryGetProperty("receipt_email", out var emailProp))
                        {
                            receiptEmail = emailProp.GetString();
                        }
                        if (string.IsNullOrWhiteSpace(receiptEmail) && payment.TryGetProperty("buyer_email_address", out var buyerEmailProp))
                        {
                            receiptEmail = buyerEmailProp.GetString();
                        }
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

    private static List<string> BuildSignatureUrls(string primaryUrl)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(primaryUrl))
            return urls.ToList();

        void AddVariants(string url)
        {
            urls.Add(url);

            // Trailing slash variants
            if (url.EndsWith('/'))
            {
                urls.Add(url.TrimEnd('/'));
            }
            else
            {
                urls.Add(url + "/");
            }

            // Port-stripped variants for :443 and :80
            if (url.Contains(":443", StringComparison.Ordinal))
            {
                urls.Add(url.Replace(":443", "", StringComparison.Ordinal));
            }
            if (url.Contains(":80", StringComparison.Ordinal))
            {
                urls.Add(url.Replace(":80", "", StringComparison.Ordinal));
            }
        }

        AddVariants(primaryUrl);

        // Scheme flip variants
        if (primaryUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            AddVariants("https://" + primaryUrl.Substring("http://".Length));
        }
        else if (primaryUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            AddVariants("http://" + primaryUrl.Substring("https://".Length));
        }

        return urls.ToList();
    }

    private static string ComputeSignatureSha1(string notificationUrl, string body, string signatureKey)
    {
        try
        {
            var payload = $"{notificationUrl}{body}";
            var keyBytes = Encoding.UTF8.GetBytes(signatureKey);
            var payloadBytes = Encoding.UTF8.GetBytes(payload);

            using var hmac = new HMACSHA1(keyBytes);
            var hash = hmac.ComputeHash(payloadBytes);
            return Convert.ToBase64String(hash);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ComputeSignatureSha256(string notificationUrl, string body, string signatureKey)
    {
        try
        {
            var payload = $"{notificationUrl}{body}";
            var keyBytes = Encoding.UTF8.GetBytes(signatureKey);
            var payloadBytes = Encoding.UTF8.GetBytes(payload);

            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(payloadBytes);
            return Convert.ToBase64String(hash);
        }
        catch
        {
            return string.Empty;
        }
    }
}
