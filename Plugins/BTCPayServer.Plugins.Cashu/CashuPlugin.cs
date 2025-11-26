using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.PaymentHandlers;
using BTCPayServer.Plugins.Cashu.Payouts.Cashu;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Cashu;
public class CashuPlugin : BaseBTCPayServerPlugin
{
    public override string Identifier => "btcpay-nuts";
    public const string PluginNavKey = nameof(CashuPlugin) + "Nav";
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new() {Identifier = nameof(BTCPayServer), Condition = ">=2.1.0"},
    };

    internal static PaymentMethodId CashuPmid = new PaymentMethodId("CASHU");
    internal static string CashuDisplayName = "Cashu";

    public override void Execute(IServiceCollection services)
    {
        services.AddTransactionLinkProvider(CashuPmid, new CashuTransactionLinkProvider("cashu"));

        services.AddSingleton(provider => 
            (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider, typeof(CashuPaymentMethodHandler)));
        services.AddSingleton(provider =>
            (ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(CashuCheckoutModelExtension)));
        services.AddDefaultPrettyName(CashuPmid, CashuDisplayName);
        
        //Cashu Singletons
        services.AddSingleton<CashuStatusProvider>();
        services.AddSingleton<CashuPaymentService>();
        
        // Payout Handler Registration
        services.AddSingleton(provider =>
            (BTCPayServer.Payouts.IPayoutHandler)ActivatorUtilities.CreateInstance(provider, typeof(Payouts.Cashu.CashuPayoutHandler)));
        
        // Payout Processor Registration
        services.AddSingleton<CashuAutomatedPayoutSenderFactory>();
        services.AddSingleton<BTCPayServer.PayoutProcessors.IPayoutProcessorFactory>(provider => 
            provider.GetRequiredService<CashuAutomatedPayoutSenderFactory>());
        
        //Ui extensions
        services.AddUIExtension("store-wallets-nav", "CashuStoreNav");
        services.AddUIExtension("checkout-payment", "CashuCheckout");

        //Database Services
        services.AddSingleton<CashuDbContextFactory>();
        services.AddDbContext<CashuDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<CashuDbContextFactory>();
            factory.ConfigureBuilder(o);
        });
        services.AddHostedService<MigrationRunner>();
            
        base.Execute(services);
    }
}

