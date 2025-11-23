using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Data;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers
{
    [AllowAnonymous]
    [Route("plugins/bitcoinrewards/webhooks")]
    public class WebhookController : Controller
    {
        private readonly StoreRepository _storeRepository;
        private readonly BitcoinRewardsService _rewardsService;
        private readonly SquareApiService _squareApiService;

        public WebhookController(
            StoreRepository storeRepository,
            BitcoinRewardsService rewardsService,
            SquareApiService squareApiService)
        {
            _storeRepository = storeRepository;
            _rewardsService = rewardsService;
            _squareApiService = squareApiService;
        }

        [HttpPost("shopify/{storeId}")]
        public async Task<IActionResult> ShopifyWebhook(string storeId)
        {
            try
            {
                var store = await _storeRepository.FindStore(storeId);
                if (store == null)
                {
                    return NotFound();
                }

                var settings = BitcoinRewards.BitcoinRewardsExtensions.GetBitcoinRewardsSettings(store.GetStoreBlob());
                if (settings == null || !settings.Enabled || !settings.ShopifyEnabled)
                {
                    return BadRequest("Bitcoin Rewards is not enabled for Shopify on this store");
                }

                // Verify webhook signature (Shopify uses HMAC SHA256)
                var hmacHeader = Request.Headers["X-Shopify-Hmac-Sha256"].ToString();
                if (!string.IsNullOrEmpty(hmacHeader) && !string.IsNullOrEmpty(settings.WebhookSecret))
                {
                    using var reader = new StreamReader(Request.Body, Encoding.UTF8);
                    var body = await reader.ReadToEndAsync();
                    var isValid = VerifyShopifyWebhook(body, hmacHeader, settings.WebhookSecret);
                    if (!isValid)
                    {
                        return Unauthorized("Invalid webhook signature");
                    }

                    Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
                }

                var json = await new StreamReader(Request.Body).ReadToEndAsync();
                var orderData = ParseShopifyOrder(json, storeId);

                if (orderData != null)
                {
                    var reward = await _rewardsService.ProcessOrderReward(orderData);
                    return Ok(new { rewardId = reward.Id, status = reward.Status.ToString() });
                }

                return BadRequest("Invalid order data");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("square/{storeId}")]
        public async Task<IActionResult> SquareWebhook(string storeId)
        {
            try
            {
                var store = await _storeRepository.FindStore(storeId);
                if (store == null)
                {
                    return NotFound();
                }

                var settings = BitcoinRewards.BitcoinRewardsExtensions.GetBitcoinRewardsSettings(store.GetStoreBlob());
                if (settings == null || !settings.Enabled || !settings.SquareEnabled)
                {
                    return BadRequest("Bitcoin Rewards is not enabled for Square on this store");
                }

                // Verify webhook signature (Square uses HMAC SHA256)
                var signatureHeader = Request.Headers["X-Square-Signature"].ToString();
                if (!string.IsNullOrEmpty(signatureHeader) && !string.IsNullOrEmpty(settings.WebhookSecret))
                {
                    using var reader = new StreamReader(Request.Body, Encoding.UTF8);
                    var body = await reader.ReadToEndAsync();
                    var isValid = VerifySquareWebhook(body, signatureHeader, settings.WebhookSecret);
                    if (!isValid)
                    {
                        return Unauthorized("Invalid webhook signature");
                    }

                    Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
                }

                var json = await new StreamReader(Request.Body).ReadToEndAsync();
                var orderData = await ParseSquareOrderAsync(json, storeId, settings);

                if (orderData != null)
                {
                    var reward = await _rewardsService.ProcessOrderReward(orderData);
                    return Ok(new { rewardId = reward.Id, status = reward.Status.ToString() });
                }

                return BadRequest("Invalid order data");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private OrderData? ParseShopifyOrder(string json, string storeId)
        {
            try
            {
                var order = JObject.Parse(json);

                return new OrderData
                {
                    OrderId = order["id"]?.ToString() ?? order["order_id"]?.ToString() ?? string.Empty,
                    OrderNumber = order["order_number"]?.ToString() ?? order["number"]?.ToString() ?? string.Empty,
                    OrderAmount = order["total_price"]?.Value<decimal>() ?? 0m,
                    Currency = order["currency"]?.ToString() ?? "USD",
                    CustomerEmail = order["email"]?.ToString() ?? order["customer"]?["email"]?.ToString() ?? string.Empty,
                    CustomerPhone = order["phone"]?.ToString() ?? order["customer"]?["phone"]?.ToString() ?? string.Empty,
                    CustomerName = $"{order["customer"]?["first_name"]} {order["customer"]?["last_name"]}".Trim(),
                    Source = "shopify",
                    StoreId = storeId
                };
            }
            catch
            {
                return null;
            }
        }

        private async Task<OrderData?> ParseSquareOrderAsync(string json, string storeId, BitcoinRewardsSettings settings)
        {
            try
            {
                var webhook = JObject.Parse(json);
                var order = webhook["data"]?["object"]?["order"] ?? webhook["order"];
                if (order == null)
                {
                    return null;
                }
                var orderId = order["id"]?.ToString() ?? string.Empty;
                var customerId = order["customer_id"]?.ToString();

                var orderData = new OrderData
                {
                    OrderId = orderId,
                    OrderNumber = order["reference_id"]?.ToString() ?? orderId,
                    OrderAmount = order["total_money"]?["amount"]?.Value<decimal>() / 100m ?? 0m, // Square uses cents
                    Currency = order["total_money"]?["currency"]?.ToString() ?? "USD",
                    CustomerEmail = string.Empty,
                    CustomerPhone = string.Empty,
                    CustomerName = string.Empty,
                    Source = "square",
                    StoreId = storeId
                };

                // Try to fetch customer info from Square API if customer ID is available and credentials are set
                if (!string.IsNullOrEmpty(customerId) && settings.SquareCredentialsPopulated())
                {
                    try
                    {
                        var customerInfo = await _squareApiService.GetCustomerInfoAsync(
                            customerId,
                            settings.SquareAccessToken,
                            settings.SquareLocationId,
                            settings.SquareEnvironment);

                        if (customerInfo != null)
                        {
                            orderData.CustomerEmail = customerInfo.Email;
                            orderData.CustomerPhone = customerInfo.Phone;
                            orderData.CustomerName = $"{customerInfo.GivenName} {customerInfo.FamilyName}".Trim();
                        }
                    }
                    catch (Exception)
                    {
                        // Log but don't fail - we'll try to get email from order data
                        // Logging is handled in SquareApiService
                    }
                }

                // Fallback: try to get email from order data directly
                if (string.IsNullOrEmpty(orderData.CustomerEmail))
                {
                    orderData.CustomerEmail = order["email_address"]?.ToString() ?? string.Empty;
                }

                return orderData;
            }
            catch
            {
                return null;
            }
        }

        private bool VerifyShopifyWebhook(string body, string hmacHeader, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
            var hashString = BitConverter.ToString(hash).Replace("-", "").ToLower();
            return hashString == hmacHeader.ToLower();
        }

        private bool VerifySquareWebhook(string body, string signatureHeader, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
            var hashString = Convert.ToBase64String(hash);
            return hashString == signatureHeader;
        }
    }
}

