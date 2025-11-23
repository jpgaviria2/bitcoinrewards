using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.BitcoinRewards.Services
{
    public class ShopifyApiService
    {
        private readonly HttpClient _httpClient;
        private readonly Logs _logs;

        public ShopifyApiService(HttpClient httpClient, Logs logs)
        {
            _httpClient = httpClient;
            _logs = logs;
        }

        public async Task<ShopifyOrderInfo?> GetOrderInfoAsync(
            string orderId,
            string shopDomain,
            string accessToken)
        {
            try
            {
                // Ensure shop domain has proper format
                var normalizedDomain = shopDomain;
                if (!normalizedDomain.StartsWith("https://"))
                {
                    normalizedDomain = $"https://{normalizedDomain}";
                }
                if (normalizedDomain.EndsWith("/"))
                {
                    normalizedDomain = normalizedDomain.TrimEnd('/');
                }

                var request = new HttpRequestMessage(HttpMethod.Get, 
                    $"{normalizedDomain}/admin/api/2024-01/orders/{orderId}.json");
                
                request.Headers.Add("X-Shopify-Access-Token", accessToken);
                request.Headers.Add("Content-Type", "application/json");

                var response = await _httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logs.PayServer.LogWarning($"Shopify API returned {response.StatusCode} for order {orderId}");
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);
                
                var order = json["order"];
                if (order == null)
                {
                    return null;
                }

                var customer = order["customer"];
                var totalPrice = order["total_price"]?.Value<decimal>() ?? 0m;
                var currency = order["currency"]?.ToString() ?? "USD";

                return new ShopifyOrderInfo
                {
                    OrderId = orderId,
                    OrderNumber = order["order_number"]?.ToString() ?? orderId,
                    TotalAmount = totalPrice,
                    Currency = currency,
                    CustomerEmail = order["email"]?.ToString() ?? customer?["email"]?.ToString() ?? string.Empty,
                    CustomerId = customer?["id"]?.ToString() ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                _logs.PayServer.LogError(ex, $"Failed to fetch Shopify order info: {ex.Message}");
                return null;
            }
        }

        public async Task<ShopifyCustomerInfo?> GetCustomerInfoAsync(
            string customerId,
            string shopDomain,
            string accessToken)
        {
            try
            {
                // Ensure shop domain has proper format
                var normalizedDomain = shopDomain;
                if (!normalizedDomain.StartsWith("https://"))
                {
                    normalizedDomain = $"https://{normalizedDomain}";
                }
                if (normalizedDomain.EndsWith("/"))
                {
                    normalizedDomain = normalizedDomain.TrimEnd('/');
                }

                var request = new HttpRequestMessage(HttpMethod.Get, 
                    $"{normalizedDomain}/admin/api/2024-01/customers/{customerId}.json");
                
                request.Headers.Add("X-Shopify-Access-Token", accessToken);
                request.Headers.Add("Content-Type", "application/json");

                var response = await _httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logs.PayServer.LogWarning($"Shopify API returned {response.StatusCode} for customer {customerId}");
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);
                
                var customer = json["customer"];
                if (customer == null)
                {
                    return null;
                }

                return new ShopifyCustomerInfo
                {
                    Email = customer["email"]?.ToString() ?? string.Empty,
                    Phone = customer["phone"]?.ToString() ?? string.Empty,
                    FirstName = customer["first_name"]?.ToString() ?? string.Empty,
                    LastName = customer["last_name"]?.ToString() ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                _logs.PayServer.LogError(ex, $"Failed to fetch Shopify customer info: {ex.Message}");
                return null;
            }
        }

        public bool VerifyWebhookSignature(string body, string signature, string secret)
        {
            try
            {
                using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secret));
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
                var hashString = BitConverter.ToString(hash).Replace("-", "").ToLower();
                return hashString == signature.ToLower();
            }
            catch (Exception ex)
            {
                _logs.PayServer.LogError(ex, $"Failed to verify Shopify webhook signature: {ex.Message}");
                return false;
            }
        }
    }

    public class ShopifyCustomerInfo
    {
        public string Email { get; set; } = null!;
        public string Phone { get; set; } = null!;
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
    }

    public class ShopifyOrderInfo
    {
        public string OrderId { get; set; } = null!;
        public string OrderNumber { get; set; } = null!;
        public decimal TotalAmount { get; set; }
        public string Currency { get; set; } = null!;
        public string CustomerEmail { get; set; } = null!;
        public string CustomerId { get; set; } = null!;
    }
}

