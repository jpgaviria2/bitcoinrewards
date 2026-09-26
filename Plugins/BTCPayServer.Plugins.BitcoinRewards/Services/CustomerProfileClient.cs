#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

public sealed class CustomerProfileClient
{
    private readonly HttpClient _httpClient;
    private readonly StoreRepository _storeRepository;

    public CustomerProfileClient(HttpClient httpClient, StoreRepository storeRepository)
    {
        _httpClient = httpClient;
        _storeRepository = storeRepository;
    }

    public sealed record ResolvedProfile(string ProfileId, string LightningAddressHash);
    private sealed record ResolveRequest([property: JsonPropertyName("lightningAddress")] string LightningAddress);
    private sealed record ResolveResponse(
        [property: JsonPropertyName("profileId")] string ProfileId,
        [property: JsonPropertyName("lightningAddress")] string LightningAddress);
    private sealed record RewardSettlementRequest(
        [property: JsonPropertyName("eventId")] string EventId,
        [property: JsonPropertyName("rewardId")] string RewardId,
        [property: JsonPropertyName("profileId")] string ProfileId,
        [property: JsonPropertyName("amountSatoshis")] long AmountSatoshis,
        [property: JsonPropertyName("occurredAt")] DateTime OccurredAt);

    public sealed class DeliveryException : Exception
    {
        public bool Retryable { get; }
        public bool Suppressed { get; }
        public DeliveryException(string message, bool retryable, bool suppressed = false) : base(message)
        {
            Retryable = retryable;
            Suppressed = suppressed;
        }
    }

    public async Task<ResolvedProfile?> ResolveAsync(
        string storeId,
        string lightningAddress,
        CancellationToken cancellationToken = default)
    {
        var settings = await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(
            storeId, BitcoinRewardsStoreSettings.SettingsName);
        if (settings?.CustomerProfileAssociationEnabled != true)
            throw new InvalidOperationException("Customer profile association is disabled for this store.");
        if (string.IsNullOrWhiteSpace(settings.CustomerProfileApiToken))
            throw new InvalidOperationException("Customer profile service credential is not configured.");

        var baseUri = ValidateBaseUri(settings.CustomerProfileApiBaseUrl);
        var normalized = NormalizeLightningAddress(lightningAddress);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(baseUri, "/api/v1/internal/rewards/resolve-lightning-address"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.CustomerProfileApiToken);
        request.Content = JsonContent.Create(new ResolveRequest(normalized));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var resolved = await response.Content.ReadFromJsonAsync<ResolveResponse>(cancellationToken: cancellationToken)
            ?? throw new HttpRequestException("Customer profile service returned an empty response.");
        var returnedAddress = NormalizeLightningAddress(resolved.LightningAddress);
        if (!string.Equals(normalized, returnedAddress, StringComparison.Ordinal))
            throw new HttpRequestException("Customer profile service returned a mismatched Lightning address.");
        if (!Guid.TryParse(resolved.ProfileId, out _))
            throw new HttpRequestException("Customer profile service returned an invalid profile identifier.");

        return new ResolvedProfile(resolved.ProfileId, AddressHash(settings.CustomerProfileApiToken, normalized));
    }

    public async Task PublishRewardSettledAsync(
        string storeId,
        string eventId,
        Guid rewardId,
        string profileId,
        long amountSatoshis,
        DateTime occurredAt,
        CancellationToken cancellationToken = default)
    {
        var settings = await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(
            storeId, BitcoinRewardsStoreSettings.SettingsName);
        if (settings?.CustomerProfileAssociationEnabled != true)
            throw new DeliveryException("Customer profile association is disabled for this store.", false, true);
        if (settings.CustomerRewardNotificationsEnabled != true)
            throw new DeliveryException("Customer reward notifications are disabled for this store.", false, true);
        if (string.IsNullOrWhiteSpace(settings.CustomerProfileApiToken))
            throw new DeliveryException("Customer profile service credential is not configured.", true);

        var baseUri = ValidateBaseUri(settings.CustomerProfileApiBaseUrl);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(baseUri, "/api/v1/internal/rewards/settled"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.CustomerProfileApiToken);
        request.Content = JsonContent.Create(new RewardSettlementRequest(
            eventId,
            rewardId.ToString("D"),
            profileId,
            amountSatoshis,
            occurredAt));

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.IsSuccessStatusCode) return;

        var status = (int)response.StatusCode;
        var retryable = status == 408 || status == 425 || status == 429 || status >= 500;
        throw new DeliveryException($"Customer reward event endpoint returned HTTP {status}.", retryable);
    }

    public static Uri ValidateBaseUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "api.trailscoffee.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            throw new InvalidOperationException("Customer profile API must use https://api.trailscoffee.com.");
        }
        return new Uri("https://api.trailscoffee.com");
    }

    public static string NormalizeLightningAddress(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 128 ||
            !System.Text.RegularExpressions.Regex.IsMatch(normalized,
                "^[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?@pay\\.trailscoffee\\.com$"))
            throw new ArgumentException("A Trails app Lightning address is required.", nameof(value));
        return normalized;
    }

    private static string AddressHash(string secret, string normalizedAddress)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(
            $"bitcoin-rewards:lightning-address\0{normalizedAddress}"))).ToLowerInvariant();
    }
}
