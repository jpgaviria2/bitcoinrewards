#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Data.Payouts.LightningLike;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.BitcoinRewards.Data;
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
    private readonly PayoutMethodHandlerDictionary _payoutHandlers;
    private readonly BitcoinRewardsPluginDbContextFactory _dbContextFactory;
    private readonly ILogger<DirectLightningPayoutService> _logger;

    public DirectLightningPayoutService(
        PullPaymentHostedService pullPaymentHostedService,
        PayoutMethodHandlerDictionary payoutHandlers,
        BitcoinRewardsPluginDbContextFactory dbContextFactory,
        ILogger<DirectLightningPayoutService> logger)
    {
        _pullPaymentHostedService = pullPaymentHostedService;
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
            attempt.State = RewardPayoutState.Paying;
            attempt.UpdatedAt = DateTime.UtcNow;
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
}

public sealed record DirectLightningPayoutResult(bool Success, string? PayoutId, string? Error)
{
    public static DirectLightningPayoutResult Queued(string payoutId) => new(true, payoutId, null);
    public static DirectLightningPayoutResult Failed(string error) => new(false, null, error);
}
