#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Data.Payouts.LightningLike;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Pays scanned Lightning-address rewards using the store's configured Lightning node.
/// This intentionally avoids BTCPay's pull-payment payout processor for direct scans,
/// because that processor can cancel or strand very small 10-30 sat payouts even when
/// the underlying LND node can pay the generated invoice directly.
/// </summary>
public sealed class DirectLightningPayoutService
{
    private readonly PullPaymentHostedService _pullPaymentHostedService;
    private readonly ApplicationDbContextFactory _applicationDbContextFactory;
    private readonly PayoutMethodHandlerDictionary _payoutHandlers;
    private readonly PaymentMethodHandlerDictionary _paymentHandlers;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly LightningNetworkOptions _lightningNetworkOptions;
    private readonly LightningClientFactoryService _lightningClientFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BitcoinRewardsPluginDbContextFactory _dbContextFactory;
    private readonly ILogger<DirectLightningPayoutService> _logger;

    public DirectLightningPayoutService(
        PullPaymentHostedService pullPaymentHostedService,
        ApplicationDbContextFactory applicationDbContextFactory,
        PayoutMethodHandlerDictionary payoutHandlers,
        PaymentMethodHandlerDictionary paymentHandlers,
        BTCPayNetworkProvider networkProvider,
        IOptions<LightningNetworkOptions> lightningNetworkOptions,
        LightningClientFactoryService lightningClientFactory,
        IHttpClientFactory httpClientFactory,
        BitcoinRewardsPluginDbContextFactory dbContextFactory,
        ILogger<DirectLightningPayoutService> logger)
    {
        _pullPaymentHostedService = pullPaymentHostedService;
        _applicationDbContextFactory = applicationDbContextFactory;
        _payoutHandlers = payoutHandlers;
        _paymentHandlers = paymentHandlers;
        _networkProvider = networkProvider;
        _lightningNetworkOptions = lightningNetworkOptions.Value;
        _lightningClientFactory = lightningClientFactory;
        _httpClientFactory = httpClientFactory;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<DirectLightningPayoutResult> QueueAsync(
        string storeId,
        BitcoinRewardRecord reward,
        string lightningAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(lightningAddress))
            return DirectLightningPayoutResult.Failed("Lightning address missing");

        await using var db = _dbContextFactory.CreateContext();
        var attempt = new RewardPayoutAttempt
        {
            RewardId = reward.Id,
            AttemptNumber = 1,
            LightningAddressHash = reward.LightningAddressHash ?? string.Empty,
            AmountSatoshis = reward.RewardAmountSatoshis,
            State = RewardPayoutState.Queued
        };
        db.RewardPayoutAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);

        var directResult = await TryPayWithStoreLightningNode(storeId, reward, lightningAddress, attempt, cancellationToken);
        if (directResult.Success)
            return directResult;

        _logger.LogWarning(
            "Direct Lightning-node reward payment failed for reward {RewardId}: {Error}. Falling back to BTCPay payout processor.",
            reward.Id,
            directResult.Error);

        return await QueueViaBtcpayPayoutProcessor(storeId, reward, lightningAddress, attempt, cancellationToken);
    }

    private async Task<DirectLightningPayoutResult> TryPayWithStoreLightningNode(
        string storeId,
        BitcoinRewardRecord reward,
        string lightningAddress,
        RewardPayoutAttempt attempt,
        CancellationToken cancellationToken)
    {
        try
        {
            var sats = reward.RewardAmountSatoshis;
            if (sats <= 0)
                return DirectLightningPayoutResult.Failed("Reward amount must be greater than zero");

            var network = _networkProvider.GetNetwork<BTCPayNetwork>("BTC");
            if (network is null)
                return DirectLightningPayoutResult.Failed("BTC network is not available");

            await using var appDb = _applicationDbContextFactory.CreateContext();
            var store = await appDb.Stores.FirstOrDefaultAsync(s => s.Id == storeId, cancellationToken);
            if (store is null)
                return DirectLightningPayoutResult.Failed("Store not found");

            var paymentMethodId = PaymentTypes.LN.GetPaymentMethodId("BTC");
            var lightningConfig = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(paymentMethodId, _paymentHandlers);
            if (lightningConfig is null || !lightningConfig.IsConfigured(network, _lightningNetworkOptions))
                return DirectLightningPayoutResult.Failed("BTC Lightning node is not configured for this store");

            var bolt11 = await ResolveLightningAddressInvoice(lightningAddress, sats, cancellationToken);
            if (string.IsNullOrWhiteSpace(bolt11))
                return DirectLightningPayoutResult.Failed("Could not resolve Lightning address invoice");

            var paymentRequest = BOLT11PaymentRequest.Parse(bolt11, network.NBitcoinNetwork);
            var lightningClient = lightningConfig.CreateLightningClient(network, _lightningNetworkOptions, _lightningClientFactory);

            attempt.State = RewardPayoutState.Paying;
            attempt.ProviderReference = paymentRequest.PaymentHash?.ToString();
            attempt.UpdatedAt = DateTime.UtcNow;
            await using (var rewardDb = _dbContextFactory.CreateContext())
            {
                rewardDb.RewardPayoutAttempts.Update(attempt);
                await rewardDb.SaveChangesAsync(cancellationToken);
            }

            var payment = await lightningClient.Pay(
                bolt11,
                new PayInvoiceParams
                {
                    Amount = new LightMoney(sats, LightMoneyUnit.Satoshi),
                    SendTimeout = TimeSpan.FromSeconds(45)
                },
                cancellationToken);

            if (payment.Result == PayResult.Ok)
            {
                var paymentHash = payment.Details?.PaymentHash?.ToString() ?? paymentRequest.PaymentHash?.ToString();
                await MarkDirectPaymentPaid(reward.Id, attempt.Id, paymentHash, cancellationToken);
                _logger.LogInformation(
                    "Paid direct Lightning reward {RewardId} for {Sats} sats through store Lightning node. Payment hash {PaymentHash}",
                    reward.Id,
                    sats,
                    paymentHash);
                return DirectLightningPayoutResult.Queued(paymentHash ?? attempt.ProviderReference ?? string.Empty, RewardPayoutState.Paid);
            }

            var existingPayment = await GetPaymentStatus(lightningClient, paymentRequest.PaymentHash?.ToString(), cancellationToken);
            if (existingPayment?.Status == LightningPaymentStatus.Complete)
            {
                var paymentHash = existingPayment.PaymentHash ?? paymentRequest.PaymentHash?.ToString();
                await MarkDirectPaymentPaid(reward.Id, attempt.Id, paymentHash, cancellationToken);
                return DirectLightningPayoutResult.Queued(paymentHash ?? attempt.ProviderReference ?? string.Empty, RewardPayoutState.Paid);
            }

            var error = payment.Result switch
            {
                PayResult.CouldNotFindRoute => payment.ErrorDetail is null
                    ? "Unable to find a Lightning route for the reward payment"
                    : $"Unable to find a Lightning route for the reward payment: {payment.ErrorDetail}",
                PayResult.Error => payment.ErrorDetail ?? "Lightning node returned a payment error",
                PayResult.Unknown => "Lightning payment status is unknown",
                _ => payment.ErrorDetail ?? $"Lightning payment failed: {payment.Result}"
            };

            await MarkDirectPaymentFailed(reward.Id, attempt.Id, error, cancellationToken);
            return DirectLightningPayoutResult.Failed(error);
        }
        catch (Exception ex)
        {
            await MarkDirectPaymentFailed(reward.Id, attempt.Id, ex.Message, cancellationToken);
            _logger.LogError(ex, "Direct Lightning-node reward payment failed for reward {RewardId}", reward.Id);
            return DirectLightningPayoutResult.Failed(ex.Message);
        }
    }

    private async Task<string> ResolveLightningAddressInvoice(string destination, long sats, CancellationToken cancellationToken)
    {
        var lnurl = destination.IsValidEmail()
            ? LNURL.LNURL.ExtractUriFromInternetIdentifier(destination)
            : LNURL.LNURL.Parse(destination, out _);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        var httpClient = CreateLnurlHttpClient(lnurl);
        var rawInfo = await LNURL.LNURL.FetchInformation(lnurl, httpClient, linkedCts.Token);
        if (rawInfo is not LNURL.LNURLPayRequest info)
            throw new InvalidOperationException("Lightning address did not return a valid LNURL-pay request");

        var amount = new LightMoney(sats, LightMoneyUnit.Satoshi);
        if (info.MinSendable is not null && amount < info.MinSendable)
            throw new InvalidOperationException($"Lightning address minimum is {info.MinSendable.ToDecimal(LightMoneyUnit.Satoshi):0} sats");
        if (info.MaxSendable is not null && amount > info.MaxSendable)
            throw new InvalidOperationException($"Lightning address maximum is {info.MaxSendable.ToDecimal(LightMoneyUnit.Satoshi):0} sats");

        var separator = info.Callback.Query.Length == 0 ? "?" : "&";
        var callbackUrl = new Uri(info.Callback + $"{separator}amount={amount.MilliSatoshi}");
        using var response = await httpClient.GetAsync(callbackUrl, linkedCts.Token);
        var responseText = await response.Content.ReadAsStringAsync(linkedCts.Token);
        if (!response.IsSuccessStatusCode)
        {
            var reason = TryReadLnurlReason(responseText) ?? response.ReasonPhrase ?? "unknown LNURL callback error";
            throw new InvalidOperationException($"Lightning address invoice callback failed: {reason}");
        }

        var callbackResponse = JObject.Parse(responseText);
        var status = callbackResponse.Value<string>("status");
        if (string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(callbackResponse.Value<string>("reason") ?? "Lightning address returned an error");

        var bolt11 = callbackResponse.Value<string>("pr");
        if (string.IsNullOrWhiteSpace(bolt11))
            throw new InvalidOperationException("Lightning address callback did not return a BOLT11 invoice");

        return bolt11;
    }

    private HttpClient CreateLnurlHttpClient(Uri uri)
    {
        return _httpClientFactory.CreateClient(uri.IsOnion()
            ? LightningLikePayoutHandler.LightningLikePayoutHandlerOnionNamedClient
            : LightningLikePayoutHandler.LightningLikePayoutHandlerClearnetNamedClient);
    }

    private static string? TryReadLnurlReason(string responseText)
    {
        try
        {
            return JObject.Parse(responseText).Value<string>("reason");
        }
        catch
        {
            return null;
        }
    }

    private async Task<LightningPayment?> GetPaymentStatus(ILightningClient lightningClient, string? paymentHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(paymentHash))
            return null;

        try
        {
            return await lightningClient.GetPayment(paymentHash, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task MarkDirectPaymentPaid(Guid rewardId, Guid attemptId, string? paymentHash, CancellationToken cancellationToken)
    {
        await using var db = _dbContextFactory.CreateContext();
        var attempt = await db.RewardPayoutAttempts.FirstOrDefaultAsync(a => a.Id == attemptId, cancellationToken);
        var reward = await db.BitcoinRewardRecords.FirstOrDefaultAsync(r => r.Id == rewardId, cancellationToken);
        if (attempt is not null)
        {
            attempt.State = RewardPayoutState.Paid;
            attempt.PaymentHash = paymentHash?.Length <= 64 ? paymentHash : paymentHash?[..64];
            attempt.ProviderReference = attempt.PaymentHash ?? attempt.ProviderReference;
            attempt.PaidAt = DateTime.UtcNow;
            attempt.UpdatedAt = DateTime.UtcNow;
            attempt.LastError = null;
        }
        if (reward is not null)
        {
            reward.DirectPayoutState = RewardPayoutState.Paid;
            reward.Status = RewardStatus.Sent;
            reward.SentAt ??= DateTime.UtcNow;
            reward.ErrorMessage = null;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkDirectPaymentFailed(Guid rewardId, Guid attemptId, string error, CancellationToken cancellationToken)
    {
        await using var db = _dbContextFactory.CreateContext();
        var attempt = await db.RewardPayoutAttempts.FirstOrDefaultAsync(a => a.Id == attemptId, cancellationToken);
        var reward = await db.BitcoinRewardRecords.FirstOrDefaultAsync(r => r.Id == rewardId, cancellationToken);
        if (attempt is not null)
        {
            attempt.State = RewardPayoutState.RetryableFailure;
            attempt.LastError = error;
            attempt.UpdatedAt = DateTime.UtcNow;
        }
        if (reward is not null)
        {
            reward.DirectPayoutState = RewardPayoutState.RetryableFailure;
            reward.ErrorMessage = error;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<DirectLightningPayoutResult> QueueViaBtcpayPayoutProcessor(
        string storeId,
        BitcoinRewardRecord reward,
        string lightningAddress,
        RewardPayoutAttempt attempt,
        CancellationToken cancellationToken)
    {
        var payoutMethodId = PayoutTypes.LN.GetPayoutMethodId("BTC");
        if (_payoutHandlers.TryGet(payoutMethodId) is not LightningLikePayoutHandler handler)
            return DirectLightningPayoutResult.Failed("BTC Lightning payout handler is not available");

        var (destination, parseError) = await handler.ParseClaimDestination(lightningAddress, cancellationToken);
        if (destination is null)
            return DirectLightningPayoutResult.Failed(parseError ?? "Invalid Lightning address");

        await using var db = _dbContextFactory.CreateContext();
        try
        {
            var sats = reward.RewardAmountSatoshis;
            var btc = sats / 100_000_000m;
            var claim = new ClaimRequest
            {
                StoreId = storeId,
                PayoutMethodId = payoutMethodId,
                Destination = destination,
                ClaimedAmount = btc,
                PreApprove = true,
                NonInteractiveOnly = true,
                Metadata = new JObject
                {
                    ["source"] = "bitcoin-rewards-square",
                    ["rewardId"] = reward.Id.ToString(),
                    ["transactionId"] = reward.TransactionId,
                    ["orderId"] = reward.OrderId
                }
            };

            attempt.State = RewardPayoutState.Paying;
            attempt.UpdatedAt = DateTime.UtcNow;
            db.RewardPayoutAttempts.Update(attempt);
            await db.SaveChangesAsync(cancellationToken);

            var response = await _pullPaymentHostedService.Claim(claim);
            if (response.Result != ClaimRequest.ClaimResult.Ok || response.PayoutData is null)
            {
                var error = ClaimRequest.GetErrorMessage(response.Result) ?? response.Result.ToString();
                attempt.State = RewardPayoutState.RetryableFailure;
                attempt.LastError = error;
                attempt.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return DirectLightningPayoutResult.Failed(error);
            }

            attempt.ProviderReference = response.PayoutData.Id;
            attempt.State = await MapPayoutState(response.PayoutData.Id, attempt, cancellationToken);
            if (attempt.State == RewardPayoutState.Paying)
            {
                attempt.State = await WaitForFinalPayoutState(response.PayoutData.Id, attempt, cancellationToken);
            }
            attempt.UpdatedAt = DateTime.UtcNow;
            reward.DirectPayoutState = attempt.State;
            if (attempt.State == RewardPayoutState.Paid)
            {
                reward.Status = RewardStatus.Sent;
                reward.SentAt ??= DateTime.UtcNow;
            }
            else if (attempt.State is RewardPayoutState.RetryableFailure or RewardPayoutState.PermanentFailure)
            {
                reward.ErrorMessage = attempt.LastError;
            }
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Queued fallback BTCPay Lightning reward payout {PayoutId} for reward {RewardId}", response.PayoutData.Id, reward.Id);
            return attempt.State switch
            {
                RewardPayoutState.Paid or RewardPayoutState.Paying => DirectLightningPayoutResult.Queued(response.PayoutData.Id, attempt.State),
                _ => DirectLightningPayoutResult.Failed(attempt.LastError ?? "BTCPay did not complete the Lightning payout")
            };
        }
        catch (Exception ex)
        {
            attempt.State = RewardPayoutState.RetryableFailure;
            attempt.LastError = ex.Message;
            attempt.UpdatedAt = DateTime.UtcNow;
            db.RewardPayoutAttempts.Update(attempt);
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogError(ex, "Failed to queue fallback BTCPay Lightning payout for reward {RewardId}", reward.Id);
            return DirectLightningPayoutResult.Failed(ex.Message);
        }
    }

    private async Task<RewardPayoutState> MapPayoutState(string payoutId, RewardPayoutAttempt attempt, CancellationToken cancellationToken)
    {
        await using var appDb = _applicationDbContextFactory.CreateContext();
        var payout = await appDb.Payouts.FirstOrDefaultAsync(p => p.Id == payoutId, cancellationToken);
        if (payout is null)
            return RewardPayoutState.Paying;

        return payout.State switch
        {
            PayoutState.Completed => MarkPaid(attempt, payout.Proof?.ToString()),
            PayoutState.Cancelled => MarkFailed(attempt, "BTCPay cancelled the Lightning payout. The amount may be below the destination or store payout minimum, or automated payout approval failed."),
            PayoutState.AwaitingPayment or PayoutState.InProgress => RewardPayoutState.Paying,
            _ => RewardPayoutState.Paying
        };
    }

    private async Task<RewardPayoutState> WaitForFinalPayoutState(string payoutId, RewardPayoutAttempt attempt, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 12; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            var state = await MapPayoutState(payoutId, attempt, cancellationToken);
            if (state is RewardPayoutState.Paid or RewardPayoutState.RetryableFailure or RewardPayoutState.PermanentFailure)
                return state;
        }

        return RewardPayoutState.Paying;
    }

    private static RewardPayoutState MarkPaid(RewardPayoutAttempt attempt, string? proof)
    {
        attempt.PaymentHash = ExtractPaymentHash(proof);
        attempt.PaidAt = DateTime.UtcNow;
        return RewardPayoutState.Paid;
    }

    private static string? ExtractPaymentHash(string? proof)
    {
        if (string.IsNullOrWhiteSpace(proof))
            return null;

        try
        {
            var token = JObject.Parse(proof);
            var paymentHash = token.Value<string>("PaymentHash") ?? token.Value<string>("Id");
            return paymentHash?.Length <= 64 ? paymentHash : paymentHash?[..64];
        }
        catch
        {
            return proof.Length <= 64 ? proof : proof[..64];
        }
    }

    private static RewardPayoutState MarkFailed(RewardPayoutAttempt attempt, string error)
    {
        attempt.LastError = error;
        return RewardPayoutState.RetryableFailure;
    }
}

public sealed record DirectLightningPayoutResult(bool Success, string? PayoutId, string? Error, RewardPayoutState? State = null)
{
    public static DirectLightningPayoutResult Queued(string payoutId, RewardPayoutState? state = null) => new(true, payoutId, null, state);
    public static DirectLightningPayoutResult Failed(string error) => new(false, null, error);
}
