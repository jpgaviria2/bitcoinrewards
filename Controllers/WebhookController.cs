using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
        private readonly SquareApiService? _squareApiService;
        private readonly ShopifyApiService? _shopifyApiService;
        private readonly Logs _logs;

        public WebhookController(
            StoreRepository storeRepository,
            BitcoinRewardsService rewardsService,
            SquareApiService? squareApiService,
            ShopifyApiService? shopifyApiService,
            Logs logs)
        {
            _storeRepository = storeRepository;
            _rewardsService = rewardsService;
            _squareApiService = squareApiService;
            _shopifyApiService = shopifyApiService;
            _logs = logs;
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
                
                // Log webhook received
                _logs.PayServer.LogInformation($"Shopify webhook received for store {storeId}");
                
                // Log service availability
                if (_shopifyApiService == null)
                {
                    _logs.PayServer.LogWarning("ShopifyApiService is not available - will use webhook data only");
                }
                
                var orderData = await ParseShopifyOrderAsync(json, storeId, settings);

                if (orderData != null)
                {
                    _logs.PayServer.LogInformation($"Processing Shopify order {orderData.OrderId} for store {storeId}");
                    var reward = await _rewardsService.ProcessOrderReward(orderData);
                    _logs.PayServer.LogInformation($"Successfully processed Shopify reward {reward.Id} for order {orderData.OrderId}");
                    return Ok(new { rewardId = reward.Id, status = reward.Status.ToString() });
                }

                _logs.PayServer.LogWarning($"Failed to parse Shopify order data for store {storeId}");
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
                
                // Log webhook received
                _logs.PayServer.LogInformation($"Square webhook received for store {storeId}");
                
                // Log service availability
                if (_squareApiService == null)
                {
                    _logs.PayServer.LogWarning("SquareApiService is not available - will use webhook data only");
                }
                
                var orderData = await ParseSquareOrderAsync(json, storeId, settings);

                if (orderData != null)
                {
                    _logs.PayServer.LogInformation($"Processing Square order {orderData.OrderId} for store {storeId}");
                    var reward = await _rewardsService.ProcessOrderReward(orderData);
                    _logs.PayServer.LogInformation($"Successfully processed Square reward {reward.Id} for order {orderData.OrderId}");
                    return Ok(new { rewardId = reward.Id, status = reward.Status.ToString() });
                }

                _logs.PayServer.LogWarning($"Failed to parse Square order data for store {storeId}");
                return BadRequest("Invalid order data");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private async Task<OrderData?> ParseShopifyOrderAsync(string json, string storeId, BitcoinRewardsSettings settings)
        {
            try
            {
                var webhook = JObject.Parse(json);
                
                // Shopify webhooks can have different structures
                // Try to find the order object in various locations
                var order = webhook["order"] ?? webhook;
                
                if (order == null)
                {
                    return null;
                }
                
                var orderId = order["id"]?.ToString() ?? order["order_id"]?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(orderId))
                {
                    return null;
                }
                
                var customerId = order["customer_id"]?.ToString() ?? order["customer"]?["id"]?.ToString();
                
                var orderData = new OrderData
                {
                    OrderId = orderId,
                    OrderNumber = order["order_number"]?.ToString() ?? order["number"]?.ToString() ?? orderId,
                    OrderAmount = order["total_price"]?.Value<decimal>() ?? 0m,
                    Currency = order["currency"]?.ToString() ?? "USD",
                    CustomerEmail = order["email"]?.ToString() ?? order["customer"]?["email"]?.ToString() ?? string.Empty,
                    CustomerPhone = order["phone"]?.ToString() ?? order["customer"]?["phone"]?.ToString() ?? string.Empty,
                    CustomerName = $"{order["customer"]?["first_name"]} {order["customer"]?["last_name"]}".Trim(),
                    Source = "shopify",
                    StoreId = storeId
                };
                
                // Try to fetch customer info from Shopify API if customer ID is available and credentials are set
                if (!string.IsNullOrEmpty(customerId) && settings.ShopifyCredentialsPopulated() && _shopifyApiService != null)
                {
                    try
                    {
                        var customerInfo = await _shopifyApiService.GetCustomerInfoAsync(
                            customerId,
                            settings.ShopifyShopDomain,
                            settings.ShopifyAccessToken);
                        
                        if (customerInfo != null)
                        {
                            // Use API data if webhook data is missing
                            if (string.IsNullOrEmpty(orderData.CustomerEmail) && !string.IsNullOrEmpty(customerInfo.Email))
                            {
                                orderData.CustomerEmail = customerInfo.Email;
                            }
                            if (string.IsNullOrEmpty(orderData.CustomerPhone) && !string.IsNullOrEmpty(customerInfo.Phone))
                            {
                                orderData.CustomerPhone = customerInfo.Phone;
                            }
                            if (string.IsNullOrEmpty(orderData.CustomerName) && 
                                (!string.IsNullOrEmpty(customerInfo.FirstName) || !string.IsNullOrEmpty(customerInfo.LastName)))
                            {
                                orderData.CustomerName = $"{customerInfo.FirstName} {customerInfo.LastName}".Trim();
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Log but don't fail - we'll use webhook data
                        // Logging is handled in ShopifyApiService
                    }
                }
                
                // Try to fetch order info from Shopify API if credentials are set and we need more data
                if (settings.ShopifyCredentialsPopulated() && _shopifyApiService != null)
                {
                    try
                    {
                        var orderInfo = await _shopifyApiService.GetOrderInfoAsync(
                            orderId,
                            settings.ShopifyShopDomain,
                            settings.ShopifyAccessToken);
                        
                        if (orderInfo != null)
                        {
                            // Use API data if webhook data is missing or incomplete
                            if (orderData.OrderAmount == 0m && orderInfo.TotalAmount > 0m)
                            {
                                orderData.OrderAmount = orderInfo.TotalAmount;
                            }
                            if (string.IsNullOrEmpty(orderData.Currency) && !string.IsNullOrEmpty(orderInfo.Currency))
                            {
                                orderData.Currency = orderInfo.Currency;
                            }
                            if (string.IsNullOrEmpty(orderData.CustomerEmail) && !string.IsNullOrEmpty(orderInfo.CustomerEmail))
                            {
                                orderData.CustomerEmail = orderInfo.CustomerEmail;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Log but don't fail - we'll use webhook data
                        // Logging is handled in ShopifyApiService
                    }
                }
                
                return orderData;
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
                if (!string.IsNullOrEmpty(customerId) && settings.SquareCredentialsPopulated() && _squareApiService != null)
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

