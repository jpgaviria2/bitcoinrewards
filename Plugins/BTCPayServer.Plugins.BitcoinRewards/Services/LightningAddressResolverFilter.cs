#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Lightning;
using BTCPayServer.Plugins;
using LNURL;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Plugin hook filter that resolves NIP-05 usernames to LNURL-pay requests.
/// This integrates with BTCPay's built-in /.well-known/lnurlp/{username} endpoint.
/// When BTCPay doesn't find a Lightning Address in its own DB, it calls this filter.
/// </summary>
public class LightningAddressResolverFilter : PluginHookFilter<LightningAddressResolver>
{
    public override string Hook => "resolve-lnurlp-request-for-lightning-address";

    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<LightningAddressResolverFilter> _logger;

    public LightningAddressResolverFilter(
        IServiceProvider serviceProvider,
        IHttpContextAccessor httpContextAccessor,
        ILogger<LightningAddressResolverFilter> logger)
    {
        _serviceProvider = serviceProvider;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public override async Task<LightningAddressResolver> Execute(LightningAddressResolver resolver)
    {
        // If another plugin already resolved this, don't override
        if (resolver.LNURLPayRequest != null)
            return resolver;

        var username = resolver.Username?.ToLowerInvariant();
        if (string.IsNullOrEmpty(username))
            return resolver;

        // Check if this is one of our NIP-05 users (scoped service, resolve from request scope)
        using var scope = _serviceProvider.CreateScope();
        var walletService = scope.ServiceProvider.GetRequiredService<CustomerWalletService>();
        var wallet = await walletService.FindByUsernameAsync(username);
        if (wallet == null)
            return resolver;

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return resolver;

        var host = httpContext.Request.Host.Value;
        var scheme = httpContext.Request.Scheme;
        var callbackUrl = $"{scheme}://{host}/plugins/bitcoin-rewards/lnurlp/{username}/callback";

        var metadata = JsonConvert.SerializeObject(new object[]
        {
            new[] { "text/plain", $"Payment to {username}@{host}" },
            new[] { "text/identifier", $"{username}@{host}" }
        });

        resolver.LNURLPayRequest = new LNURLPayRequest
        {
            Tag = "payRequest",
            Callback = new Uri(callbackUrl),
            MinSendable = new LightMoney(1000, LightMoneyUnit.MilliSatoshi),   // 1 sat
            MaxSendable = new LightMoney(100000000000, LightMoneyUnit.MilliSatoshi), // 100k sats
            Metadata = metadata,
            CommentAllowed = 255,
            AllowsNostr = true,
            NostrPubkey = wallet.Pubkey ?? ""
        };

        _logger.LogInformation("Resolved Lightning Address for NIP-05 user {Username}", username);
        return resolver;
    }
}
