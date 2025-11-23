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
    public class SquareApiService
    {
        private readonly HttpClient _httpClient;
        private readonly Logs _logs;

        public SquareApiService(HttpClient httpClient, Logs logs)
        {
            _httpClient = httpClient;
            _logs = logs;
        }

        public async Task<SquareCustomerInfo> GetCustomerInfoAsync(
            string customerId,
            string accessToken,
            string locationId,
            string environment = "production")
        {
            try
            {
                var baseUrl = environment == "sandbox" 
                    ? "https://connect.squareupsandbox.com" 
                    : "https://connect.squareup.com";

                var request = new HttpRequestMessage(HttpMethod.Get, 
                    $"{baseUrl}/v2/customers/{customerId}");
                
                request.Headers.Add("Authorization", $"Bearer {accessToken}");
                request.Headers.Add("Square-Version", "2023-10-18");

                var response = await _httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logs.PayServer.LogWarning($"Square API returned {response.StatusCode} for customer {customerId}");
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);
                
                var customer = json["customer"];
                if (customer == null)
                {
                    return null;
                }

                return new SquareCustomerInfo
                {
                    Email = customer["email_address"]?.ToString(),
                    Phone = customer["phone_number"]?.ToString(),
                    GivenName = customer["given_name"]?.ToString(),
                    FamilyName = customer["family_name"]?.ToString()
                };
            }
            catch (Exception ex)
            {
                _logs.PayServer.LogError(ex, $"Failed to fetch Square customer info: {ex.Message}");
                return null;
            }
        }

        public async Task<SquareOrderInfo> GetOrderInfoAsync(
            string orderId,
            string accessToken,
            string locationId,
            string environment = "production")
        {
            try
            {
                var baseUrl = environment == "sandbox" 
                    ? "https://connect.squareupsandbox.com" 
                    : "https://connect.squareup.com";

                var request = new HttpRequestMessage(HttpMethod.Get, 
                    $"{baseUrl}/v2/orders/{orderId}");
                
                request.Headers.Add("Authorization", $"Bearer {accessToken}");
                request.Headers.Add("Square-Version", "2023-10-18");

                var response = await _httpClient.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logs.PayServer.LogWarning($"Square API returned {response.StatusCode} for order {orderId}");
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);
                
                var order = json["order"];
                if (order == null)
                {
                    return null;
                }

                var totalMoney = order["total_money"];
                var customerId = order["customer_id"]?.ToString();

                return new SquareOrderInfo
                {
                    OrderId = orderId,
                    CustomerId = customerId,
                    TotalAmount = totalMoney?["amount"]?.Value<decimal>() / 100m ?? 0m,
                    Currency = totalMoney?["currency"]?.ToString() ?? "USD"
                };
            }
            catch (Exception ex)
            {
                _logs.PayServer.LogError(ex, $"Failed to fetch Square order info: {ex.Message}");
                return null;
            }
        }
    }

    public class SquareCustomerInfo
    {
        public string Email { get; set; }
        public string Phone { get; set; }
        public string GivenName { get; set; }
        public string FamilyName { get; set; }
    }

    public class SquareOrderInfo
    {
        public string OrderId { get; set; }
        public string CustomerId { get; set; }
        public decimal TotalAmount { get; set; }
        public string Currency { get; set; }
    }
}

