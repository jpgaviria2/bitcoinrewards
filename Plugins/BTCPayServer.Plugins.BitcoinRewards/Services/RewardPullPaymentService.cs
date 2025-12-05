#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.BitcoinRewards.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly ILogger<RewardPullPaymentService> _logger;

    public RewardPullPaymentService(
        IServiceProvider serviceProvider,
        LinkGenerator linkGenerator,
        IHttpContextAccessor httpContextAccessor,
        PayoutProcessorDiscoveryService payoutProcessorDiscoveryService,
        ILogger<RewardPullPaymentService> logger)
    {
        _serviceProvider = serviceProvider;
        _linkGenerator = linkGenerator;
        _httpContextAccessor = httpContextAccessor;
        _payoutProcessorDiscoveryService = payoutProcessorDiscoveryService;
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

            var pullPaymentServiceType = Type.GetType("BTCPayServer.HostedServices.PullPaymentHostedService, BTCPayServer");
            if (pullPaymentServiceType == null)
            {
                _logger.LogError("PullPaymentHostedService type not found in BTCPayServer assembly");
                return new PullPaymentResult(false, Error: "Pull payment service not available");
            }

            var pullPaymentService = _serviceProvider.GetService(pullPaymentServiceType);
            if (pullPaymentService == null)
            {
                _logger.LogError("PullPaymentHostedService not resolved from DI");
                return new PullPaymentResult(false, Error: "Pull payment service not available");
            }

            var createRequestType = pullPaymentServiceType.GetNestedType("CreatePullPayment", BindingFlags.Public | BindingFlags.NonPublic);
            if (createRequestType == null)
            {
                _logger.LogError("CreatePullPayment request type not found on PullPaymentHostedService");
                return new PullPaymentResult(false, Error: "Pull payment request type not available");
            }

            var request = Activator.CreateInstance(createRequestType);
            if (request == null)
            {
                return new PullPaymentResult(false, Error: "Failed to instantiate pull payment request");
            }

            var paymentMethods = new List<PaymentMethodId> { selection.PaymentMethodId };
            SetProp(createRequestType, request, "StoreId", storeId);
            SetProp(createRequestType, request, "Name", "Bitcoin Reward");
            SetProp(createRequestType, request, "Description", description);
            SetProp(createRequestType, request, "Amount", rewardSats / 100_000_000m);
            SetProp(createRequestType, request, "Currency", "BTC");
            SetProp(createRequestType, request, "AutoApproveClaims", true);
            SetProp(createRequestType, request, "PaymentMethodIds", paymentMethods);

            // Optional niceties if the BTCPay version supports them
            SetProp(createRequestType, request, "StartsAt", DateTimeOffset.UtcNow);
            SetProp(createRequestType, request, "ExpiresAt", DateTimeOffset.UtcNow.AddDays(30));

            var createMethod = pullPaymentServiceType.GetMethod("CreatePullPayment", BindingFlags.Public | BindingFlags.Instance);
            if (createMethod == null)
            {
                _logger.LogError("CreatePullPayment method not found on PullPaymentHostedService");
                return new PullPaymentResult(false, Error: "Pull payment method missing");
            }

            object? invocationResult;
            var parameters = createMethod.GetParameters();
            if (parameters.Length == 2 && parameters[1].ParameterType == typeof(CancellationToken))
            {
                invocationResult = createMethod.Invoke(pullPaymentService, new[] { request, (object)cancellationToken });
            }
            else
            {
                invocationResult = createMethod.Invoke(pullPaymentService, new[] { request });
            }

            if (invocationResult is not Task task)
            {
                _logger.LogError("CreatePullPayment invocation did not return a Task");
                return new PullPaymentResult(false, Error: "Unexpected pull payment response");
            }

            await task;

            var resultObj = task.GetType().GetProperty("Result")?.GetValue(task);
            var pullPaymentId = resultObj?.GetType().GetProperty("PullPaymentId")?.GetValue(resultObj)?.ToString();
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

    private static void SetProp(Type type, object instance, string propertyName, object? value)
    {
        var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null || !prop.CanWrite)
            return;

        if (value == null)
        {
            prop.SetValue(instance, null);
            return;
        }

        if (prop.PropertyType.IsInstanceOfType(value))
        {
            prop.SetValue(instance, value);
            return;
        }

        // Try simple conversion for common cases (e.g., decimal to nullable decimal)
        try
        {
            var converted = Convert.ChangeType(value, Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);
            prop.SetValue(instance, converted);
        }
        catch
        {
            // If conversion fails, just skip silently to avoid crashing; optional fields are best-effort.
        }
    }

    private class PayoutSelection
    {
        public string Processor { get; set; } = string.Empty;
        public PayoutMethodId PayoutMethodId { get; set; } = null!;
        public PaymentMethodId PaymentMethodId { get; set; } = null!;
    }
}

