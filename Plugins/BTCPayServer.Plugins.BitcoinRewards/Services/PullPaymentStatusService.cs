#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using Microsoft.Extensions.Logging;
using PayoutState = BTCPayServer.Client.Models.PayoutState;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Service to check the status of BTCPay pull payments
/// </summary>
public class PullPaymentStatusService
{
    private readonly PullPaymentHostedService _pullPaymentService;
    private readonly ILogger<PullPaymentStatusService> _logger;

    public PullPaymentStatusService(
        PullPaymentHostedService pullPaymentService,
        ILogger<PullPaymentStatusService> logger)
    {
        _pullPaymentService = pullPaymentService;
        _logger = logger;
    }

    /// <summary>
    /// Check if a pull payment has been fully claimed (all payouts are completed)
    /// </summary>
    public async Task<bool> IsPullPaymentClaimedAsync(string pullPaymentId)
    {
        try
        {
            var pullPayment = await _pullPaymentService.GetPullPayment(pullPaymentId, includePayouts: true);
            
            if (pullPayment == null)
            {
                _logger.LogWarning("Pull payment {PullPaymentId} not found", pullPaymentId);
                return false;
            }

            // If there are no payouts, it's not claimed
            if (pullPayment.Payouts == null || !pullPayment.Payouts.Any())
            {
                return false;
            }

            // Check if all payouts are completed or cancelled
            var allCompleted = pullPayment.Payouts.All(p => 
                p.State == PayoutState.Completed || p.State == PayoutState.Cancelled);

            // If all payouts are completed/cancelled and there's at least one completed, consider it claimed
            var hasCompleted = pullPayment.Payouts.Any(p => p.State == PayoutState.Completed);

            return allCompleted && hasCompleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking pull payment status for {PullPaymentId}", pullPaymentId);
            return false;
        }
    }
}

