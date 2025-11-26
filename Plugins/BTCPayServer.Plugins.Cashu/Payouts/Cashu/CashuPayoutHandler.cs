#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using BTCPayServer.Plugins.Cashu.PaymentHandlers;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PayoutData = BTCPayServer.Data.PayoutData;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Cashu.Payouts.Cashu;

/// <summary>
/// Payout handler for Cashu payouts.
/// Handles parsing of Cashu token destinations and manages payout proofs.
/// </summary>
public class CashuPayoutHandler : IPayoutHandler
{
    private readonly PaymentMethodHandlerDictionary _paymentHandlers;
    private readonly BTCPayNetworkJsonSerializerSettings _jsonSerializerSettings;

    public CashuPayoutHandler(
        PaymentMethodHandlerDictionary paymentHandlers,
        BTCPayNetworkJsonSerializerSettings jsonSerializerSettings)
    {
        _paymentHandlers = paymentHandlers;
        _jsonSerializerSettings = jsonSerializerSettings;
        PayoutMethodId = PayoutMethodId.Parse(CashuPlugin.CashuPmid.ToString());
        PaymentMethodId = CashuPlugin.CashuPmid;
        Currency = "BTC"; // Cashu is BTC-denominated
    }

    public PayoutMethodId PayoutMethodId { get; }
    public PaymentMethodId PaymentMethodId { get; }
    public string Currency { get; }

    public bool IsSupported(StoreData storeData)
    {
        // Check if Cashu payment method is configured for the store
        var config = storeData.GetPaymentMethodConfig<CashuPaymentMethodConfig>(
            CashuPlugin.CashuPmid, _paymentHandlers);
        return config != null && config.TrustedMintsUrls != null && config.TrustedMintsUrls.Count > 0;
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

        // For Cashu, destination can be:
        // 1. A Cashu token string (for receiving tokens)
        // 2. An email address (to send token to)
        // 3. A phone number (to send token via SMS)
        
        // Try parsing as Cashu token first
        if (CashuUtils.TryDecodeToken(destination, out var token))
        {
            // Valid Cashu token - create a claim destination
            return Task.FromResult<(IClaimDestination, string)>(
                (new CashuTokenClaimDestination(destination), null!));
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
            return null!;

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
