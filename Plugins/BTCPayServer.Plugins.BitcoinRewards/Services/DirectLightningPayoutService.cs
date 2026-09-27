#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Data.Payouts.LightningLike;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Creates an idempotent BTCPay Lightning payout directly to a scanned Lightning address.
/// BTCPay's payout pipeline handles validation, approval, and automated payment when the
/// store has a Lightning payout processor configured.
/// </summary>
public sealed class DirectLightningPayoutService
{
    private readonly PullPaymentHostedService _pullPaymentHostedService;
    private readonly ApplicationDbContextFactory _applicationDbContextFactory;
    private readonly PayoutMethodHandlerDictionary _payoutHandlers;
    private readonly BitcoinRewardsPluginDbContextFactory _dbContextFactory;
    private readonly ILogger<DirectLightningPayoutService> _logger;

    public DirectLightningPayoutService(
        PullPaymentHostedService pullPaymentHostedService,
        ApplicationDbContextFactory applicationDbContextFactory,
        PayoutMethodHandlerDictionary payoutHandlers,
        BitcoinRewardsPluginDbContextFactory dbContextFactory,
        ILogger<DirectLightningPayoutService> logger)
    {
        _pullPaymentHostedService = pullPaymentHostedService;
        _applicationDbContextFactory = applicationDbContextFactory;
        _payoutHandlers = payoutHandlers;
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

        var payoutMethodId = PayoutTypes.LN.GetPayoutMethodId("BTC");
        if (_payoutHandlers.TryGet(payoutMethodId) is not LightningLikePayoutHandler handler)
            return DirectLightningPayoutResult.Failed("BTC Lightning payout handler is not available");

        var (destination, parseError) = await handler.ParseClaimDestination(lightningAddress, cancellationToken);
        if (destination is null)
            return DirectLightningPayoutResult.Failed(parseError ?? "Invalid Lightning address");

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
            if (attempt.State == RewardPayoutState.Paying && reward.TransactionId.StartsWith("SCAN_TEST_", StringComparison.Ordinal))
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
            _logger.LogInformation("Queued direct Lightning reward payout {PayoutId} for reward {RewardId}", response.PayoutData.Id, reward.Id);
            return DirectLightningPayoutResult.Queued(response.PayoutData.Id);
        }
        catch (Exception ex)
        {
            attempt.State = RewardPayoutState.RetryableFailure;
            attempt.LastError = ex.Message;
            attempt.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogError(ex, "Failed to queue direct Lightning payout for reward {RewardId}", reward.Id);
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

public sealed record DirectLightningPayoutResult(bool Success, string? PayoutId, string? Error)
{
    public static DirectLightningPayoutResult Queued(string payoutId) => new(true, payoutId, null);
    public static DirectLightningPayoutResult Failed(string error) => new(false, null, error);
}
