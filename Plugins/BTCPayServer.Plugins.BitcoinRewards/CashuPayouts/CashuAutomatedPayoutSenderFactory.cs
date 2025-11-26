#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.PayoutProcessors;
using BTCPayServer.Payouts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.BitcoinRewards.CashuPayouts;

/// <summary>
/// Factory for creating Cashu automated payout processors.
/// Uses reflection to work with BTCNutServer Cashu plugin services.
/// </summary>
public class CashuAutomatedPayoutSenderFactory : IPayoutProcessorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly LinkGenerator _linkGenerator;

    public CashuAutomatedPayoutSenderFactory(
        IServiceProvider serviceProvider,
        LinkGenerator linkGenerator)
    {
        _serviceProvider = serviceProvider;
        _linkGenerator = linkGenerator;
    }

    public string Processor => ProcessorName;
    public static string ProcessorName => "BitcoinRewardsCashuAutomatedPayoutSender";

    public string FriendlyName => "Cashu Automated Payout Sender";

    public IEnumerable<PayoutMethodId> GetSupportedPayoutMethods()
    {
        // Use Cashu payment method ID: "CASHU"
        // This must match BTCNutServer's CashuPlugin.CashuPmid
        yield return PayoutMethodId.Parse("CASHU");
    }

    public Task<IHostedService> ConstructProcessor(PayoutProcessorData settings)
    {
        return Task.FromResult<IHostedService>(ActivatorUtilities.CreateInstance<CashuAutomatedPayoutProcessor>(_serviceProvider, settings));
    }

    public string ConfigureLink(string storeId, PayoutMethodId payoutMethodId, HttpRequest request)
    {
        return _linkGenerator.GetUriByAction("Configure",
            "UICashuAutomatedPayoutProcessors", new
            {
                storeId
            }, request.Scheme, request.Host, request.PathBase) ?? string.Empty;
    }
}

