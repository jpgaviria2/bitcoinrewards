#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PayoutData = BTCPayServer.Data.PayoutData;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.BitcoinRewards.CashuPayouts;

/// <summary>
/// Payout handler for Cashu payouts in Bitcoin Rewards plugin.
/// Uses reflection to access BTCNutServer's Cashu utilities.
/// Handles parsing of Cashu token destinations and manages payout proofs.
/// </summary>
public class CashuPayoutHandler : IPayoutHandler
{
    private readonly BTCPayServer.Services.Invoices.PaymentMethodHandlerDictionary _paymentHandlers;
    private readonly BTCPayNetworkJsonSerializerSettings _jsonSerializerSettings;
    private readonly ILogger<CashuPayoutHandler> _logger;
    private readonly IServiceProvider _serviceProvider;

    // Cashu Payment Method ID - must match BTCNutServer's
    private static readonly PaymentMethodId CashuPmid = new PaymentMethodId("CASHU");

    public CashuPayoutHandler(
        BTCPayServer.Services.Invoices.PaymentMethodHandlerDictionary paymentHandlers,
        BTCPayNetworkJsonSerializerSettings jsonSerializerSettings,
        ILogger<CashuPayoutHandler> logger,
        IServiceProvider serviceProvider)
    {
        _paymentHandlers = paymentHandlers;
        _jsonSerializerSettings = jsonSerializerSettings;
        _logger = logger;
        _serviceProvider = serviceProvider;
        PayoutMethodId = PayoutMethodId.Parse(CashuPmid.ToString());
        PaymentMethodId = CashuPmid;
        Currency = "BTC"; // Cashu is BTC-denominated
    }

    public PayoutMethodId PayoutMethodId { get; }
    public PaymentMethodId PaymentMethodId { get; }
    public string Currency { get; }

    public bool IsSupported(StoreData storeData)
    {
        // Check if Cashu payment method is configured for the store
        // Use the non-generic GetPaymentMethodConfig extension method
        try
        {
            var config = storeData.GetPaymentMethodConfig(CashuPmid, _paymentHandlers);
            if (config == null)
                return false;

            var trustedMintsProperty = config.GetType().GetProperty("TrustedMintsUrls");
            var trustedMints = trustedMintsProperty?.GetValue(config) as List<string>;
            
            return trustedMints != null && trustedMints.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if Cashu is supported for store");
            return false;
        }
    }

    public Task TrackClaim(ClaimRequest claimRequest, PayoutData payoutData)
    {
        // No tracking needed for Cashu tokens
        return Task.CompletedTask;
    }

    public Task<(IClaimDestination destination, string error)> ParseClaimDestination(string destination, CancellationToken cancellationToken)
    {
        destination = destination?.Trim();
        if (string.IsNullOrEmpty(destination))
        {
            return Task.FromResult<(IClaimDestination, string)>((null!, "Destination cannot be empty"));
        }

        // Try to use CashuUtils.TryDecodeToken from BTCNutServer via reflection
        try
        {
            var cashuAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "btcnutserver-test" || 
                                    a.GetName().Name == "BTCPayServer.Plugins.Cashu" ||
                                    a.FullName?.Contains("Cashu") == true);
            
            if (cashuAssembly != null)
            {
                var cashuUtilsType = cashuAssembly.GetType("BTCPayServer.Plugins.Cashu.CashuAbstractions.CashuUtils");
                if (cashuUtilsType != null)
                {
                    var tryDecodeTokenMethod = cashuUtilsType.GetMethod("TryDecodeToken", 
                        new[] { typeof(string), typeof(object).MakeByRefType() });
                    
                    if (tryDecodeTokenMethod != null)
                    {
                        var tokenPlaceholder = (object?)null;
                        var parameters = new object[] { destination, tokenPlaceholder! };
                        var result = tryDecodeTokenMethod.Invoke(null, parameters);
                        
                        if (result is bool isValid && isValid)
                        {
                            // Valid Cashu token
                            return Task.FromResult<(IClaimDestination, string)>(
                                (new CashuTokenClaimDestination(destination), null!));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking if destination is Cashu token");
        }

        // Try parsing as email
        if (destination.Contains("@") && destination.Contains("."))
        {
            return Task.FromResult<(IClaimDestination, string)>(
                (new CashuEmailClaimDestination(destination), null!));
        }

        // Default: treat as a generic Cashu destination (could be phone number or other identifier)
        return Task.FromResult<(IClaimDestination, string)>(
            (new CashuGenericClaimDestination(destination), null!));
    }

    public (bool valid, string? error) ValidateClaimDestination(IClaimDestination claimDestination, PullPaymentBlob? pullPaymentBlob)
    {
        // For now, accept all Cashu destinations
        return (true, null);
    }

    public IPayoutProof ParseProof(PayoutData payout)
    {
        if (payout?.Proof == null)
            return null;

        try
        {
            var proof = JObject.Parse(payout.Proof);
            var proofType = proof["ProofType"]?.ToString();

            if (proofType == CashuPayoutBlob.CashuPayoutBlobProofType)
            {
                return proof.ToObject<CashuPayoutBlob>(
                    JsonSerializer.Create(_jsonSerializerSettings.GetSerializer(PayoutMethodId)))!;
            }
        }
        catch
        {
            // Invalid proof format
        }

        return null!;
    }

    public void StartBackgroundCheck(Action<Type[]> subscribe)
    {
        // No background checks needed for Cashu
    }

    public Task BackgroundCheck(object o)
    {
        return Task.CompletedTask;
    }

    public Task<decimal> GetMinimumPayoutAmount(IClaimDestination claimDestination)
    {
        // Minimum payout amount in BTC (1 satoshi)
        return Task.FromResult(0.00000001m);
    }

    public Dictionary<PayoutState, List<(string Action, string Text)>> GetPayoutSpecificActions()
    {
        return new Dictionary<PayoutState, List<(string, string)>>();
    }

    public Task<StatusMessageModel> DoSpecificAction(string action, string[] payoutIds, string storeId)
    {
        return Task.FromResult(new StatusMessageModel
        {
            Message = "Action not supported",
            Severity = StatusMessageModel.StatusSeverity.Error
        });
    }

    public Task<IActionResult> InitiatePayment(string[] payoutIds)
    {
        throw new NotImplementedException("Manual payment initiation not yet implemented for Cashu");
    }
}

// Claim destination classes for Cashu
public class CashuTokenClaimDestination : IClaimDestination
{
    public CashuTokenClaimDestination(string token)
    {
        Id = token;
    }

    public string? Id { get; }
    public decimal? Amount { get; set; }
}

public class CashuEmailClaimDestination : IClaimDestination
{
    public CashuEmailClaimDestination(string email)
    {
        Id = email;
    }

    public string? Id { get; }
    public decimal? Amount { get; set; }
}

public class CashuGenericClaimDestination : IClaimDestination
{
    public CashuGenericClaimDestination(string identifier)
    {
        Id = identifier;
    }

    public string? Id { get; }
    public decimal? Amount { get; set; }
}

