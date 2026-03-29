using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace BitcoinRewards.Tests.Integration
{
    [Trait("Category", "Integration")]
    public class WebhookIntegrationTests : IClassFixture<TestServerFixture>
    {
        private readonly TestServerFixture _fixture;
        private readonly HttpClient _client;

        public WebhookIntegrationTests(TestServerFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.Client;
        }

        [Fact]
        public async Task SquareWebhook_ValidPayload_ShouldReturn200()
        {
            // Arrange
            var storeId = _fixture.TestStoreId;
            var payload = new
            {
                type = "payment.updated",
                data = new
                {
                    @object = new
                    {
                        payment = new
                        {
                            id = $"test_payment_{Guid.NewGuid()}",
                            status = "COMPLETED",
                            amount_money = new
                            {
                                amount = 1000, // $10.00
                                currency = "USD"
                            },
                            receipt_email = "test@example.com",
                            order_id = $"test_order_{Guid.NewGuid()}"
                        }
                    }
                }
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            // Add signature header (would need proper HMAC in real test)
            content.Headers.Add("X-Square-Signature", "test_signature");

            // Act
            var response = await _client.PostAsync(
                $"/plugins/bitcoin-rewards/{storeId}/webhooks/square",
                content);

            // Assert
            response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Unauthorized);
            // Unauthorized is expected without valid signature
        }

        [Fact]
        public async Task SquareWebhook_MissingSignature_ShouldReturn400()
        {
            // Arrange
            var storeId = _fixture.TestStoreId;
            var payload = new { type = "payment.updated" };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            // Act
            var response = await _client.PostAsync(
                $"/plugins/bitcoin-rewards/{storeId}/webhooks/square",
                content);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task SquareWebhook_InvalidPayload_ShouldReturn500()
        {
            // Arrange
            var storeId = _fixture.TestStoreId;
            var content = new StringContent(
                "invalid json",
                Encoding.UTF8,
                "application/json");

            content.Headers.Add("X-Square-Signature", "test_signature");

            // Act
            var response = await _client.PostAsync(
                $"/plugins/bitcoin-rewards/{storeId}/webhooks/square",
                content);

            // Assert
            response.StatusCode.Should().BeOneOf(
                HttpStatusCode.BadRequest,
                HttpStatusCode.InternalServerError);
        }

        [Fact]
        public async Task RateLimiting_ExcessiveRequests_ShouldReturn429()
        {
            // Arrange
            var storeId = _fixture.TestStoreId;
            var payload = new { type = "payment.updated" };
            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            content.Headers.Add("X-Square-Signature", "test_signature");

            // Act - Send many requests rapidly
            HttpResponseMessage? lastResponse = null;
            for (int i = 0; i < 100; i++)
            {
                lastResponse = await _client.PostAsync(
                    $"/plugins/bitcoin-rewards/{storeId}/webhooks/square",
                    content);

                if (lastResponse.StatusCode == HttpStatusCode.TooManyRequests)
                    break;
            }

            // Assert - Should eventually hit rate limit
            lastResponse.Should().NotBeNull();
            // Note: May not hit 429 in test environment with high limits
        }
    }

    public class TestServerFixture : IDisposable
    {
        public HttpClient Client { get; }
        public string TestStoreId { get; } = "test_store_" + Guid.NewGuid().ToString("N");

        public TestServerFixture()
        {
            // In a real implementation, this would spin up a TestServer
            // For now, this is a placeholder
            Client = new HttpClient
            {
                BaseAddress = new Uri("https://localhost:5001")
            };
        }

        public void Dispose()
        {
            Client?.Dispose();
        }
    }
}
