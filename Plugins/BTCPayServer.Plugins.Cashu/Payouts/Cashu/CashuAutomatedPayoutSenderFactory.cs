#nullable enable
using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.PayoutProcessors;
using BTCPayServer.Payouts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.Cashu.Payouts.Cashu;

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
    public static string ProcessorName => nameof(CashuAutomatedPayoutSenderFactory);

    public string FriendlyName => "Cashu Automated Payout Sender";

    public IEnumerable<PayoutMethodId> GetSupportedPayoutMethods()
    {
        // Return Cashu payout method ID
        // For Cashu, we use the payment method ID directly as the payout method ID
        // Format: "CASHU"
        yield return PayoutMethodId.Parse(CashuPlugin.CashuPmid.ToString());
    }

    public Task<IHostedService> ConstructProcessor(PayoutProcessorData settings)
    {
        return Task.FromResult<IHostedService>(ActivatorUtilities.CreateInstance<CashuAutomatedPayoutProcessor>(_serviceProvider, settings));
    }

    public string ConfigureLink(string storeId, PayoutMethodId payoutMethodId, HttpRequest request)
    {
        // Return empty string for now - configuration can be done via the payout processor settings UI
        // Can be enhanced later to point to a specific configuration page
        return string.Empty;
    }
}
