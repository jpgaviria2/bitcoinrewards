#nullable enable
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace BTCPayServer.Plugins.BitcoinRewards.Clients;

public class SquareApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly string _applicationId;
    private readonly string _accessToken;
    private readonly string _locationId;
    private readonly string _environment;

    public SquareApiClient(
        HttpClient httpClient,
        string applicationId,
        string accessToken,
        string locationId,
        string environment,
        ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _applicationId = applicationId;
        _accessToken = accessToken;
        _locationId = locationId;
        _environment = environment;

        var baseUrl = environment == "sandbox" 
            ? "https://connect.squareupsandbox.com" 
            : "https://connect.squareup.com";
        
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", accessToken);
        _httpClient.DefaultRequestHeaders.Add("Square-Version", "2024-01-18");
    }

    public async Task<SquareTransaction?> GetTransactionAsync(string transactionId)
    {
        try
        {
            var url = $"/v2/payments/{transactionId}";
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Square API error: {StatusCode} - {Reason}", 
                    response.StatusCode, response.ReasonPhrase);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);
            var payment = jsonDoc.RootElement.GetProperty("payment");
            
            return new SquareTransaction
            {
                Id = payment.GetProperty("id").GetString() ?? transactionId,
                Amount = payment.TryGetProperty("amount_money", out var amountMoney) 
                    ? amountMoney.TryGetProperty("amount", out var amount) ? amount.GetInt64() : 0 
                    : 0,
                Currency = payment.TryGetProperty("amount_money", out var am) 
                    ? am.TryGetProperty("currency", out var curr) ? curr.GetString() ?? "USD" : "USD"
                    : "USD",
                Status = payment.TryGetProperty("status", out var status) ? status.GetString() : null,
                ReceiptEmail = payment.TryGetProperty("receipt_email", out var email) ? email.GetString() : null,
                ReceiptPhone = payment.TryGetProperty("receipt_phone", out var phone) ? phone.GetString() : null,
                OrderId = payment.TryGetProperty("order_id", out var orderId) ? orderId.GetString() : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Square transaction {TransactionId}", transactionId);
            return null;
        }
    }

    public bool VerifyWebhookSignature(string notificationUrl, string requestBody, string signature, string signatureKey)
    {
        if (string.IsNullOrEmpty(signatureKey) || string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(notificationUrl))
        {
            _logger.LogWarning("Square webhook verification skipped due to missing data (url or signature key).");
            return false;
        }

        try
        {
            // Per Square docs: HMAC-SHA256 over notificationUrl + requestBody, Base64 encoded
            var payload = $"{notificationUrl}{requestBody}";
            var keyBytes = Encoding.UTF8.GetBytes(signatureKey);
            var payloadBytes = Encoding.UTF8.GetBytes(payload);

            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(payloadBytes);
            var computedSignature = Convert.ToBase64String(hash);

            var matches = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedSignature),
                Encoding.UTF8.GetBytes(signature));

            if (!matches)
            {
                _logger.LogWarning("Square webhook signature mismatch.");
            }

            return matches;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying Square webhook signature.");
            return false;
        }
    }
}

public class SquareTransaction
{
    public string Id { get; set; } = string.Empty;
    public long Amount { get; set; } // In smallest currency unit (cents for USD)
    public string Currency { get; set; } = "USD";
    public string? Status { get; set; }
    public string? ReceiptEmail { get; set; }
    public string? ReceiptPhone { get; set; }
    public string? OrderId { get; set; }
}

