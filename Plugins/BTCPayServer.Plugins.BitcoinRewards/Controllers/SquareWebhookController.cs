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

    // Helper to mask sensitive URL parts
    private static string MaskUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return "***";
        var uri = new Uri(url);
        return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
    }

    [HttpPost]
    [RequestSizeLimit(1_048_576)] // Security: 1 MB limit
    public async Task<IActionResult> HandleWebhook(string storeId)
    {
        // Security: Rate limiting - prevent webhook flooding
        if (!await BitcoinRewardsPlugin.WebhookProcessingLock.WaitAsync(TimeSpan.FromSeconds(2)))
        {
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            _logger.LogWarning("🚨 SECURITY: Webhook processing capacity exceeded from IP {IP} for store {StoreId}", 
                clientIp, storeId);
            return StatusCode(429, "Too many requests - please try again later");
        }
        
        try
        {
            // Security: Read request body with size validation
            string requestBody;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                const int maxSize = 1_048_576; // 1 MB
                var buffer = new char[8192];
                var sb = new StringBuilder();
                int totalRead = 0;
                int read;
                
                while ((read = await reader.ReadAsync(buffer, 0, Math.Min(buffer.Length, maxSize - totalRead))) > 0)
                {
                    totalRead += read;
                    if (totalRead > maxSize)
                    {
                        _logger.LogWarning("🚨 SECURITY: Oversized webhook payload from IP {IP}", 
                            HttpContext.Connection.RemoteIpAddress);
                        return BadRequest("Payload too large");
                    }
                    sb.Append(buffer, 0, read);
                }
                
                requestBody = sb.ToString();
            }

            // Get webhook signature from headers
            var signature = Request.Headers["X-Square-Signature"].ToString()?.Trim();
            if (string.IsNullOrEmpty(signature))
            {
                _logger.LogWarning("🚨 SECURITY: Square webhook missing signature from IP {RemoteIP} for store {StoreId}", 
                    HttpContext.Connection.RemoteIpAddress, storeId);
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
                _logger.LogWarning("🚨 SECURITY: Square webhook signature key not configured for store {StoreId}, rejecting webhook from IP {RemoteIP}", 
                    storeId, HttpContext.Connection.RemoteIpAddress);
                return Unauthorized();
            }

            // Security: Check ALL candidates in constant time to prevent timing attacks
            var verified = false;
            string? firstComputedSha1 = null;
            string? firstComputedSha256 = null;
            bool[] results = new bool[candidateUrls.Count];
            
            for (int i = 0; i < candidateUrls.Count; i++)
            {
                var url = candidateUrls[i];
                var computedSha1 = ComputeSignatureSha1(url, requestBody, signatureKey);
                var computedSha256 = ComputeSignatureSha256(url, requestBody, signatureKey);
                firstComputedSha1 ??= computedSha1;
                firstComputedSha256 ??= computedSha256;

                results[i] = 
                    (!string.IsNullOrEmpty(computedSha1) &&
                     CryptographicOperations.FixedTimeEquals(
                         Encoding.UTF8.GetBytes(computedSha1),
                         Encoding.UTF8.GetBytes(signature))) ||
                    (!string.IsNullOrEmpty(computedSha256) &&
                     CryptographicOperations.FixedTimeEquals(
                         Encoding.UTF8.GetBytes(computedSha256),
                         Encoding.UTF8.GetBytes(signature)));
            }
            
            // Check results after all computations complete
            verified = results.Any(r => r);

            if (!verified)
            {
                // Mask sensitive data in production logs
                var maskedSignature = signature?.Length > 8 ? signature.Substring(0, 4) + "..." + signature.Substring(signature.Length - 4) : "***";
                var maskedUrls = candidateUrls.Select(u => MaskUrl(u)).ToList();
                var maskedSha1 = firstComputedSha1?.Length > 8 ? firstComputedSha1.Substring(0, 4) + "..." : "n/a";
                var maskedSha256 = firstComputedSha256?.Length > 8 ? firstComputedSha256.Substring(0, 4) + "..." : "n/a";
                
                _logger.LogWarning("🚨 SECURITY: Square webhook signature verification failed for store {StoreId} from IP {RemoteIP} (sig={Sig}, urls tried: {Count}, computedSha1={Sha1}, computedSha256={Sha256})",
                    storeId, HttpContext.Connection.RemoteIpAddress, maskedSignature, candidateUrls.Count, maskedSha1, maskedSha256);
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
                        // Square amounts are in smallest currency units (cents), convert to major units (dollars)
                        var amount = amountMoney.TryGetProperty("amount", out var amountProp)
                            ? amountProp.GetInt64() / 100.0m
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

                        // Log high-value transactions for monitoring
                        if (amount > 1000) // > $1000
                        {
                            _logger.LogInformation("⚠️ High-value Square transaction detected: {Amount} {Currency} for store {StoreId}, payment {PaymentId}",
                                amount, currency, storeId, paymentId);
                        }

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
        finally
        {
            // Security: Always release rate limiting lock
            BitcoinRewardsPlugin.WebhookProcessingLock.Release();
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

#if DEBUG
    /// <summary>
    /// Test endpoint that bypasses signature validation - FOR TESTING ONLY
    /// POST /plugins/bitcoin-rewards/{storeId}/webhooks/square/test
    /// </summary>
    [HttpPost("test")]
    [Authorize(Policy = BTCPayServer.Client.Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> HandleTestWebhook(string storeId)
    {
        try
        {
            // Security: Validate store exists and user has access
            var store = await _storeRepository.FindStore(storeId);
            if (store == null)
            {
                _logger.LogWarning("🚨 SECURITY: Test webhook attempted on non-existent store {StoreId}", storeId);
                return NotFound($"Store {storeId} not found");
            }
            
            // Security: Verify user has access to this store
            if (!string.IsNullOrEmpty(User.Identity?.Name))
            {
                _logger.LogWarning("⚠️ TEST WEBHOOK USED in store {StoreId} by user {User} - signature validation bypassed", 
                    storeId, User.Identity.Name);
            }
            
            _logger.LogInformation("🧪 TEST: Square webhook test endpoint called for store {StoreId}", storeId);

            // Read request body
            string requestBody;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            _logger.LogInformation("🧪 TEST: Request body: {Body}", requestBody);

            // Parse Square webhook
            var jsonDoc = JsonDocument.Parse(requestBody);
            var root = jsonDoc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
            {
                return BadRequest("Missing 'type' field");
            }

            var eventType = typeProp.GetString();
            _logger.LogInformation("🧪 TEST: Event type: {EventType}", eventType);

            if (eventType != "payment.updated")
            {
                _logger.LogInformation("🧪 TEST: Ignoring event type {EventType}", eventType);
                return Ok("Event ignored");
            }

            if (root.TryGetProperty("data", out var data) &&
                data.TryGetProperty("object", out var obj) &&
                obj.TryGetProperty("payment", out var payment))
            {
                var paymentId = payment.TryGetProperty("id", out var idProp) 
                    ? idProp.GetString() ?? "unknown" 
                    : "unknown";
                var status = payment.TryGetProperty("status", out var statusProp) 
                    ? statusProp.GetString() ?? ""
                    : "";

                _logger.LogInformation("🧪 TEST: Payment ID: {PaymentId}, Status: {Status}", paymentId, status);

                if (status == "COMPLETED" && payment.TryGetProperty("amount_money", out var amountMoney))
                {
                    var amount = amountMoney.TryGetProperty("amount", out var amountProp)
                        ? amountProp.GetInt64() / 100.0m
                        : 0;
                    var currency = amountMoney.TryGetProperty("currency", out var currencyProp)
                        ? currencyProp.GetString() ?? "USD"
                        : "USD";

                    var receiptEmail = payment.TryGetProperty("receipt_email", out var emailProp) 
                        ? emailProp.GetString() 
                        : null;
                    var orderId = payment.TryGetProperty("order_id", out var orderIdProp) 
                        ? orderIdProp.GetString() 
                        : null;

                    _logger.LogInformation("🧪 TEST: Amount: {Amount} {Currency}, Email: {Email}", 
                        amount, currency, receiptEmail ?? "none");

                    var transaction = new TransactionData
                    {
                        TransactionId = paymentId,
                        OrderId = orderId,
                        Amount = (decimal)amount,
                        Currency = currency ?? "USD",
                        CustomerEmail = receiptEmail,
                        Platform = TransactionPlatform.Square,
                        TransactionDate = DateTime.UtcNow
                    };

                    _logger.LogInformation("🧪 TEST: Calling ProcessRewardAsync...");
                    var result = await _rewardsService.ProcessRewardAsync(storeId, transaction);
                    _logger.LogInformation("🧪 TEST: ProcessRewardAsync returned: {Result}", result);

                    return Ok(new
                    {
                        success = true,
                        processed = result,
                        transactionId = paymentId,
                        amount,
                        currency,
                        message = "Test webhook processed (signature check bypassed)"
                    });
                }
            }

            return Ok("Test webhook received");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🧪 TEST: Error processing test Square webhook for store {StoreId}", storeId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
#endif
}
