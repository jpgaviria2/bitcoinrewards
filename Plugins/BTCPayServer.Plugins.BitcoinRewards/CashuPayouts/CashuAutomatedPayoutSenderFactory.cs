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
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.BitcoinRewards.CashuPayouts;

/// <summary>
/// Factory for creating Cashu automated payout processors.
/// Cloned from LightningAutomatedPayoutSenderFactory pattern.
/// </summary>
public class CashuAutomatedPayoutSenderFactory : IPayoutProcessorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly LinkGenerator _linkGenerator;
    private readonly PayoutMethodId _supportedPayoutMethod;
    private IStringLocalizer StringLocalizer { get; }

    public CashuAutomatedPayoutSenderFactory(
        IServiceProvider serviceProvider,
        IStringLocalizer stringLocalizer,
        LinkGenerator linkGenerator)
    {
        _serviceProvider = serviceProvider;
        _linkGenerator = linkGenerator;
        _supportedPayoutMethod = PayoutMethodId.Parse("CASHU");
        StringLocalizer = stringLocalizer;
    }

    public string FriendlyName => StringLocalizer["Cashu Automated Payout Sender"];

    public string ConfigureLink(string storeId, PayoutMethodId payoutMethodId, HttpRequest request)
    {
        return _linkGenerator.GetUriByAction("Configure",
            "UICashuAutomatedPayoutProcessors", new
            {
                storeId
            }, request.Scheme, request.Host, request.PathBase) ?? string.Empty;
    }

    public string Processor => ProcessorName;
    public static string ProcessorName => nameof(CashuAutomatedPayoutSenderFactory);

    public IEnumerable<PayoutMethodId> GetSupportedPayoutMethods() => new[] { _supportedPayoutMethod };

    public CashuAutomatedPayoutProcessor ConstructProcessor(PayoutProcessorData settings)
    {
        if (settings.Processor != Processor)
        {
            throw new NotSupportedException("This processor cannot handle the provided requirements");
        }
        var payoutMethodId = settings.GetPayoutMethodId();
        return ActivatorUtilities.CreateInstance<CashuAutomatedPayoutProcessor>(_serviceProvider, settings, payoutMethodId);
    }

    Task<IHostedService> IPayoutProcessorFactory.ConstructProcessor(PayoutProcessorData settings)
    {
        return Task.FromResult<IHostedService>(ConstructProcessor(settings));
    }
}

