using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Plugins.BitcoinRewards;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.BitcoinRewards.PaymentHandlers;

/// <summary>
/// Payment method handler for Bitcoin Rewards Wallet.
/// This handler is used for configuration purposes only - the wallet is not used for actual invoice payments.
/// </summary>
public class WalletPaymentMethodHandler(
    BTCPayNetworkProvider networkProvider)
    : IPaymentMethodHandler, IHasNetwork
{
    private readonly BTCPayNetwork _network = networkProvider.GetNetwork<BTCPayNetwork>("BTC");
    public PaymentMethodId PaymentMethodId => BitcoinRewardsPlugin.WalletPmid;
    
    public BTCPayNetwork Network => _network;
    
    /// <summary>
    /// Configure payment prompt - throws exception since wallet is config-only, not for payments
    /// </summary>
    public Task ConfigurePrompt(PaymentMethodContext context)
    {
        // Wallet payment method is for configuration only, not for actual invoice payments
        throw new PaymentMethodUnavailableException("Bitcoin Rewards Wallet is for configuration only and cannot be used for invoice payments");
    }

    /// <summary>
    /// Set currency and divisibility before fetching rates
    /// </summary>
    public Task BeforeFetchingRates(PaymentMethodContext context)
    {
        context.Prompt.Currency = "BTC";
        context.Prompt.PaymentMethodFee = 0m;
        context.Prompt.Divisibility = 8;
        return Task.CompletedTask;
    }

    /// <summary>
    /// JSON serializer for parsing config
    /// </summary>
    public JsonSerializer Serializer { get; } = BlobSerializer.CreateSerializer().Serializer;
    
    /// <summary>
    /// Parse payment prompt details (not used for wallet)
    /// </summary>
    public object ParsePaymentPromptDetails(JToken details)
    {
        return details.ToObject<WalletPaymentMethodDetails>(Serializer) ?? new WalletPaymentMethodDetails();
    }

    /// <summary>
    /// Parse payment method config from JToken
    /// </summary>
    public object ParsePaymentMethodConfig(JToken config)
    {
        return config.ToObject<WalletPaymentMethodConfig>(Serializer) ??
               throw new FormatException($"Invalid {nameof(WalletPaymentMethodHandler)} configuration");
    }

    /// <summary>
    /// Parse payment details (not used for wallet)
    /// </summary>
    public object ParsePaymentDetails(JToken details)
    {
        return details.ToObject<WalletPaymentData>(Serializer) ?? new WalletPaymentData();
    }

    /// <summary>
    /// Strip details for non-owner (not used for wallet)
    /// </summary>
    public void StripDetailsForNonOwner(object details)
    {
        // No sensitive data to strip for wallet config
    }
}

/// <summary>
/// Payment data class (not used for wallet, but required by interface)
/// </summary>
public class WalletPaymentData
{
    // Empty - wallet is not used for actual payments
}

/// <summary>
/// Payment method details class (not used for wallet, but required by interface)
/// </summary>
public class WalletPaymentMethodDetails
{
    // Empty - wallet is not used for actual payments
}

