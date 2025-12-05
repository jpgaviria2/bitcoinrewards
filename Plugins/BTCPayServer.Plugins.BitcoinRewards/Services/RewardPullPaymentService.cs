#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.BitcoinRewards.ViewModels;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Helper to create pull payments for issued rewards using whatever payout method/processor is available.
/// Uses reflection to avoid tight coupling to BTCPayServer internals.
/// </summary>
public class RewardPullPaymentService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly LinkGenerator _linkGenerator;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PayoutProcessorDiscoveryService _payoutProcessorDiscoveryService;
    private readonly PullPaymentHostedService _pullPaymentHostedService;
    private readonly StoreRepository _storeRepository;
    private readonly ILogger<RewardPullPaymentService> _logger;

    public RewardPullPaymentService(
        IServiceProvider serviceProvider,
        LinkGenerator linkGenerator,
        IHttpContextAccessor httpContextAccessor,
        PayoutProcessorDiscoveryService payoutProcessorDiscoveryService,
        PullPaymentHostedService pullPaymentHostedService,
        StoreRepository storeRepository,
        ILogger<RewardPullPaymentService> logger)
    {
        _serviceProvider = serviceProvider;
        _linkGenerator = linkGenerator;
        _httpContextAccessor = httpContextAccessor;
        _payoutProcessorDiscoveryService = payoutProcessorDiscoveryService;
        _pullPaymentHostedService = pullPaymentHostedService;
        _storeRepository = storeRepository;
        _logger = logger;
    }

    public record PullPaymentResult(
        bool Success,
        string? PullPaymentId = null,
        string? ClaimLink = null,
        string? PayoutProcessor = null,
        string? PayoutMethod = null,
        string? Error = null);

    /// <summary>
    /// Create a pull payment for a reward amount (sats) and return the claim link/details.
    /// </summary>
    public async Task<PullPaymentResult> CreatePullPaymentAsync(
        string storeId,
        long rewardSats,
        string? preferredProcessorId,
        string description,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var selection = await SelectPayoutAsync(storeId, preferredProcessorId);
            if (selection == null)
            {
                return new PullPaymentResult(false, Error: "No payout processor available for pull payment creation");
            }

            var store = await _storeRepository.FindStore(storeId);
            if (store is null)
            {
                _logger.LogError("Store {StoreId} not found while creating pull payment", storeId);
                return new PullPaymentResult(false, Error: "Store not found");
            }

            var request = new CreatePullPaymentRequest
            {
                Name = "Bitcoin Reward",
                Description = description,
                Amount = rewardSats / 100_000_000m,
                Currency = "BTC",
                AutoApproveClaims = true,
                StartsAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
                PayoutMethods = new[] { selection.PayoutMethodId.ToString() }
            };

            var pullPaymentId = await _pullPaymentHostedService.CreatePullPayment(store, request);
            if (string.IsNullOrEmpty(pullPaymentId))
            {
                _logger.LogError("Pull payment created but PullPaymentId is missing");
                return new PullPaymentResult(false, Error: "Pull payment id missing");
            }

            var claimLink = BuildClaimLink(pullPaymentId);

            _logger.LogInformation("Created pull payment {PullPaymentId} for reward (store {StoreId}, sats {Sats}) using {Processor}/{PayoutMethod}",
                pullPaymentId, storeId, rewardSats, selection.Processor, selection.PayoutMethodId);

            return new PullPaymentResult(true,
                PullPaymentId: pullPaymentId,
                ClaimLink: claimLink,
                PayoutProcessor: selection.Processor,
                PayoutMethod: selection.PayoutMethodId.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create pull payment for reward in store {StoreId}", storeId);
            return new PullPaymentResult(false, Error: ex.Message);
        }
    }

    private string BuildClaimLink(string pullPaymentId)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            var link = _linkGenerator.GetUriByAction(httpContext, "ViewPullPayment", "UIPullPayment",
                new { pullPaymentId }, httpContext.Request.Scheme, httpContext.Request.Host, httpContext.Request.PathBase);
            if (!string.IsNullOrEmpty(link))
            {
                return link;
            }
        }

        // Fallback to relative path
        return $"/pull-payments/{pullPaymentId}";
    }

    private async Task<PayoutSelection?> SelectPayoutAsync(string storeId, string? preferredProcessorId)
    {
        var configured = await _payoutProcessorDiscoveryService.GetConfiguredPayoutProcessorsAsync(storeId);
        var available = await _payoutProcessorDiscoveryService.GetAvailablePayoutProcessorsAsync(storeId);

        // Try preferred processor if provided
        if (!string.IsNullOrEmpty(preferredProcessorId))
        {
            var preferred = configured.FirstOrDefault(p => p.ProcessorId == preferredProcessorId)
                            ?? available.FirstOrDefault(p => string.Equals(p.ProcessorId, preferredProcessorId, StringComparison.OrdinalIgnoreCase));
            if (preferred != null)
            {
                var selection = ToSelection(preferred, preferredProcessorId);
                if (selection != null)
                    return selection;
            }
        }

        // Otherwise pick the first configured processor
        var configuredSelection = configured
            .Select(o => ToSelection(o, o.ProcessorId))
            .FirstOrDefault(o => o != null);
        if (configuredSelection != null)
            return configuredSelection;

        // Fallback to any available option
        var availableSelection = available
            .Select(o => ToSelection(o, o.ProcessorId))
            .FirstOrDefault(o => o != null);

        return availableSelection;
    }

    private PayoutSelection? ToSelection(PayoutProcessorOption option, string? processorId)
    {
        if (option == null)
            return null;

        var payoutMethodId = option.SupportedMethods?.FirstOrDefault();
        if (payoutMethodId == null)
            return null;

        PaymentMethodId paymentMethodId;
        try
        {
            paymentMethodId = PaymentMethodId.Parse(payoutMethodId.ToString());
        }
        catch
        {
            _logger.LogWarning("Failed to parse PaymentMethodId from payout method {PayoutMethod}", payoutMethodId);
            return null;
        }

        var processor = !string.IsNullOrEmpty(processorId)
            ? processorId.Split(':').FirstOrDefault() ?? option.FactoryName
            : option.FactoryName;

        return new PayoutSelection
        {
            Processor = processor,
            PayoutMethodId = payoutMethodId,
            PaymentMethodId = paymentMethodId
        };
    }

    private class PayoutSelection
    {
        public string Processor { get; set; } = string.Empty;
        public PayoutMethodId PayoutMethodId { get; set; } = null!;
        public PaymentMethodId PaymentMethodId { get; set; } = null!;
    }
}

