#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers;

/// <summary>
/// LNURL-pay (LUD-16) endpoints: gives every NIP-05 user a Lightning Address.
/// username@btcpay.anmore.me resolves via /.well-known/lnurlp/{username}
/// </summary>
[ApiController]
public class LnurlpController : ControllerBase
{
    private readonly Nip05Service _nip05;
    private readonly CustomerWalletService _walletService;
    private readonly ExchangeRateService _exchangeRateService;
    private readonly BitcoinRewardsPluginDbContextFactory _dbFactory;
    private readonly StoreRepository _storeRepository;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly PaymentMethodHandlerDictionary _paymentHandlers;
    private readonly ILogger<LnurlpController> _logger;

    public LnurlpController(
        Nip05Service nip05,
        CustomerWalletService walletService,
        ExchangeRateService exchangeRateService,
        BitcoinRewardsPluginDbContextFactory dbFactory,
        StoreRepository storeRepository,
        BTCPayNetworkProvider networkProvider,
        PaymentMethodHandlerDictionary paymentHandlers,
        ILogger<LnurlpController> logger)
    {
        _nip05 = nip05;
        _walletService = walletService;
        _exchangeRateService = exchangeRateService;
        _dbFactory = dbFactory;
        _storeRepository = storeRepository;
        _networkProvider = networkProvider;
        _paymentHandlers = paymentHandlers;
        _logger = logger;
    }

    /// <summary>
    /// LNURL-pay metadata endpoint (LUD-06/LUD-16).
    /// GET /plugins/bitcoin-rewards/lnurlp/{username}
    /// For Lightning Address resolution, BTCPay's built-in /.well-known/lnurlp/{username}
    /// must be configured to point here (or use nginx rewrite).
    /// </summary>
    [HttpGet("plugins/bitcoin-rewards/lnurlp/{username}")]
    [AllowAnonymous]
    public async Task<IActionResult> LnurlPayMetadata(string username)
    {
        SetCorsHeaders();
        return await LnurlPayMetadataInternal(username);
    }

    /// <summary>
    /// CORS preflight for LNURL endpoints.
    /// </summary>
    [HttpOptions("plugins/bitcoin-rewards/lnurlp/{username}")]
    [HttpOptions("plugins/bitcoin-rewards/lnurlp/{username}/callback")]
    [AllowAnonymous]
    public IActionResult LnurlCors()
    {
        SetCorsHeaders();
        return Ok();
    }

    private void SetCorsHeaders()
    {
        Response.Headers["Access-Control-Allow-Origin"] = "*";
        Response.Headers["Access-Control-Allow-Methods"] = "GET, OPTIONS";
        Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
    }

    private async Task<IActionResult> LnurlPayMetadataInternal(string username)
    {
        var lower = username.ToLowerInvariant();

        // Find user wallet with this username
        var wallet = await _walletService.FindByUsernameAsync(lower);
        if (wallet == null)
            return NotFound(new { status = "ERROR", reason = "User not found" });

        var host = Request.Host.Value;
        var scheme = Request.Scheme;
        var callbackUrl = $"{scheme}://{host}/plugins/bitcoin-rewards/lnurlp/{lower}/callback";

        var metadata = JsonConvert.SerializeObject(new object[]
        {
            new[] { "text/plain", $"Payment to {lower}@{host}" },
            new[] { "text/identifier", $"{lower}@{host}" }
        });

        var response = new
        {
            tag = "payRequest",
            callback = callbackUrl,
            minSendable = 1000L,          // 1 sat in millisats
            maxSendable = 100000000000L,   // 100k sats in millisats
            metadata,
            commentAllowed = 255,
            allowsNostr = true,
            nostrPubkey = wallet.Pubkey ?? ""
        };

        return Ok(response);
    }

    /// <summary>
    /// LNURL-pay callback — generates a BOLT11 invoice for the user's wallet.
    /// GET /plugins/bitcoin-rewards/lnurlp/{username}/callback?amount={millisats}
    /// </summary>
    [HttpGet("plugins/bitcoin-rewards/lnurlp/{username}/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> LnurlPayCallbackWrapper(
        string username,
        [FromQuery] long amount,
        [FromQuery] string? comment = null,
        [FromQuery] string? senderWallet = null)
    {
        SetCorsHeaders();
        return await LnurlPayCallback(username, amount, comment, senderWallet);
    }

    private async Task<IActionResult> LnurlPayCallback(
        string username,
        long amount,
        string? comment = null,
        string? senderWallet = null)
    {
        var lower = username.ToLowerInvariant();

        if (amount < 1000)
            return Ok(new { status = "ERROR", reason = "Amount too low (min 1000 millisats = 1 sat)" });
        if (amount > 100000000000L)
            return Ok(new { status = "ERROR", reason = "Amount too high (max 100000000000 millisats = 100k sats)" });

        // Find recipient wallet
        var wallet = await _walletService.FindByUsernameAsync(lower);
        if (wallet == null)
            return Ok(new { status = "ERROR", reason = "User not found" });

        var sats = amount / 1000;

        // Smart internal transfer detection
        if (!string.IsNullOrWhiteSpace(senderWallet) && Guid.TryParse(senderWallet, out var senderWalletId))
        {
            // Check if sender is also a local wallet
            var senderW = await _walletService.FindByIdAsync(senderWalletId);
            if (senderW != null)
            {
                // Both wallets are local — signal internal transfer
                return Ok(new
                {
                    status = "OK",
                    internalTransfer = true,
                    toUsername = lower,
                    amountSats = sats
                });
            }
        }

        // External payment — create a Lightning invoice
        var network = _networkProvider.GetNetwork<BTCPayNetwork>("BTC");
        if (network == null)
            return Ok(new { status = "ERROR", reason = "BTC network not configured" });

        var lightningClient = await GetLightningClientAsync(wallet.StoreId, network);
        if (lightningClient == null)
            return Ok(new { status = "ERROR", reason = "Lightning not configured" });

        var host = Request.Host.Value;
        var metadata = JsonConvert.SerializeObject(new object[]
        {
            new[] { "text/plain", $"Payment to {lower}@{host}" },
            new[] { "text/identifier", $"{lower}@{host}" }
        });

        try
        {
            var invoice = await lightningClient.CreateInvoice(
                new LightMoney(amount, LightMoneyUnit.MilliSatoshi),
                metadata,
                TimeSpan.FromMinutes(10),
                CancellationToken.None);

            if (string.IsNullOrEmpty(invoice.BOLT11))
                return Ok(new { status = "ERROR", reason = "Failed to create invoice" });

            // Save pending claim so the watcher can credit the wallet on settlement
            await using var db = _dbFactory.CreateContext();
            var pendingClaim = new PendingLnurlClaim
            {
                Id = Guid.NewGuid(),
                CustomerWalletId = wallet.Id,
                StoreId = wallet.StoreId,
                LightningInvoiceId = invoice.Id,
                Bolt11 = invoice.BOLT11,
                ExpectedSats = sats,
                K1Prefix = "lnurlp",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                IsCompleted = false,
                IsFailed = false
            };
            db.PendingLnurlClaims.Add(pendingClaim);
            await db.SaveChangesAsync();

            _logger.LogInformation("LNURL-pay: created invoice for {Username}, {Sats} sats, invoice {InvoiceId}",
                lower, sats, invoice.Id);

            return Ok(new
            {
                status = "OK",
                pr = invoice.BOLT11,
                routes = Array.Empty<object>(),
                successAction = new
                {
                    tag = "message",
                    message = $"Payment received by {lower}@{host}"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LNURL-pay callback failed for {Username}", lower);
            return Ok(new { status = "ERROR", reason = "Failed to create invoice" });
        }
    }

    private async Task<ILightningClient?> GetLightningClientAsync(string storeId, BTCPayNetwork network)
    {
        var store = await _storeRepository.FindStore(storeId);
        if (store == null) return null;

        var id = PaymentTypes.LN.GetPaymentMethodId(network.CryptoCode);
        if (!_paymentHandlers.TryGetValue(id, out var handler) || handler is not LightningLikePaymentHandler lnHandler)
            return null;

        var existing = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(id, _paymentHandlers);
        if (existing == null) return null;

        return lnHandler.CreateLightningClient(existing);
    }
}
