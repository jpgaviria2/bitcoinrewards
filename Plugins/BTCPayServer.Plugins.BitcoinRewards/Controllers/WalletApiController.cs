#nullable enable
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers;

/// <summary>
/// REST API for dual-balance customer wallets.
/// Public endpoints use wallet bearer token auth.
/// Admin endpoints use BTCPay cookie/API key auth.
/// </summary>
[ApiController]
public class WalletApiController : ControllerBase
{
    private readonly CustomerWalletService _walletService;
    private readonly BoltCardRewardService _boltCardService;
    private readonly ExchangeRateService _exchangeRateService;
    private readonly PullPaymentHostedService _pullPaymentHostedService;
    private readonly PayoutMethodHandlerDictionary _payoutHandlers;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly StoreRepository _storeRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WalletApiController> _logger;

    public WalletApiController(
        CustomerWalletService walletService,
        BoltCardRewardService boltCardService,
        ExchangeRateService exchangeRateService,
        PullPaymentHostedService pullPaymentHostedService,
        PayoutMethodHandlerDictionary payoutHandlers,
        BTCPayNetworkProvider networkProvider,
        StoreRepository storeRepository,
        IHttpClientFactory httpClientFactory,
        ILogger<WalletApiController> logger)
    {
        _walletService = walletService;
        _boltCardService = boltCardService;
        _exchangeRateService = exchangeRateService;
        _pullPaymentHostedService = pullPaymentHostedService;
        _payoutHandlers = payoutHandlers;
        _networkProvider = networkProvider;
        _storeRepository = storeRepository;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // ── Helper: extract wallet from bearer token ──

    private async Task<CustomerWallet?> AuthenticateWalletAsync()
    {
        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (auth == null || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        var token = auth["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token)) return null;

        try
        {
            var hash = CustomerWalletService.HashToken(token);
            return await _walletService.FindByTokenHashAsync(hash);
        }
        catch
        {
            return null;
        }
    }

    private async Task<(CustomerWallet? wallet, IActionResult? error)> AuthAndFindWalletAsync(Guid walletId)
    {
        var wallet = await AuthenticateWalletAsync();
        if (wallet == null)
            return (null, Unauthorized(new { error = "Invalid or missing wallet token" }));
        if (wallet.Id != walletId)
            return (null, Forbid());
        return (wallet, null);
    }

    // ── Public endpoints (wallet token auth) ──

    [HttpGet("plugins/bitcoin-rewards/wallet/{walletId}/balance")]
    [AllowAnonymous]
    public async Task<IActionResult> GetBalance(Guid walletId)
    {
        var (wallet, err) = await AuthAndFindWalletAsync(walletId);
        if (err != null) return err;

        var balance = await _walletService.GetBalanceAsync(walletId);
        if (balance == null) return NotFound(new { error = "Wallet not found" });

        return Ok(new
        {
            walletId,
            satsBalance = balance.SatsBalance,
            cadBalanceCents = balance.CadBalanceCents,
            autoConvert = balance.AutoConvertToCad,
            totalRewardedSats = balance.TotalRewardedSatoshis,
            totalRewardedCadCents = balance.TotalRewardedCadCents
        });
    }

    [HttpPost("plugins/bitcoin-rewards/wallet/{walletId}/swap")]
    [AllowAnonymous]
    public async Task<IActionResult> Swap(Guid walletId, [FromBody] SwapRequest request)
    {
        var (wallet, err) = await AuthAndFindWalletAsync(walletId);
        if (err != null) return err;

        if (request.Amount <= 0)
            return BadRequest(new { error = "Amount must be positive" });

        (bool success, string? error) result;

        if (request.Direction == "to_cad")
            result = await _walletService.SwapToCadAsync(walletId, request.Amount, wallet!.StoreId);
        else if (request.Direction == "to_sats")
            result = await _walletService.SwapToSatsAsync(walletId, request.Amount, wallet!.StoreId);
        else
            return BadRequest(new { error = "Direction must be 'to_cad' or 'to_sats'" });

        if (!result.success)
            return BadRequest(new { error = result.error });

        var newBalance = await _walletService.GetBalanceAsync(walletId);
        return Ok(new
        {
            success = true,
            satsBalance = newBalance?.SatsBalance ?? 0,
            cadBalanceCents = newBalance?.CadBalanceCents ?? 0
        });
    }

    [HttpPost("plugins/bitcoin-rewards/wallet/{walletId}/settings")]
    [AllowAnonymous]
    public async Task<IActionResult> UpdateSettings(Guid walletId, [FromBody] WalletSettingsRequest request)
    {
        var (wallet, err) = await AuthAndFindWalletAsync(walletId);
        if (err != null) return err;

        var ok = await _walletService.SetAutoConvertAsync(walletId, request.AutoConvert);
        if (!ok) return NotFound(new { error = "Wallet not found" });

        return Ok(new { success = true, autoConvert = request.AutoConvert });
    }

    [HttpGet("plugins/bitcoin-rewards/wallet/{walletId}/history")]
    [AllowAnonymous]
    public async Task<IActionResult> GetHistory(Guid walletId)
    {
        var (wallet, err) = await AuthAndFindWalletAsync(walletId);
        if (err != null) return err;

        var txs = await _walletService.GetHistoryAsync(walletId);
        return Ok(txs.Select(t => new
        {
            id = t.Id,
            type = t.Type.ToString(),
            satsAmount = t.SatsAmount,
            cadCentsAmount = t.CadCentsAmount,
            exchangeRate = t.ExchangeRate,
            reference = t.Reference,
            createdAt = t.CreatedAt
        }));
    }

    // ── Pay Lightning invoice from CAD balance ──

    [HttpPost("plugins/bitcoin-rewards/wallet/{walletId}/pay-invoice")]
    [AllowAnonymous]
    public async Task<IActionResult> PayInvoice(Guid walletId, [FromBody] PayInvoiceRequest request)
    {
        var (wallet, err) = await AuthAndFindWalletAsync(walletId);
        if (err != null) return err;

        if (string.IsNullOrWhiteSpace(request.Invoice))
            return BadRequest(new { error = "Invoice is required" });

        var invoice = request.Invoice.Trim();

        // Strip lightning: URI prefix if present
        if (invoice.StartsWith("lightning:", StringComparison.OrdinalIgnoreCase))
            invoice = invoice["lightning:".Length..];

        // 1. Decode the BOLT11 invoice to get the sats amount
        var network = _networkProvider.GetNetwork<BTCPayNetwork>("BTC");
        if (network == null)
            return StatusCode(500, new { error = "BTC network not configured" });

        if (!BOLT11PaymentRequest.TryParse(invoice, out var bolt11, network.NBitcoinNetwork) || bolt11 == null)
            return BadRequest(new { error = "Invalid BOLT11 Lightning invoice" });

        if (bolt11.ExpiryDate.UtcDateTime < DateTime.UtcNow)
            return BadRequest(new { error = "Invoice has expired" });

        if (bolt11.MinimumAmount == LightMoney.Zero || bolt11.MinimumAmount == null)
            return BadRequest(new { error = "Invoice has no amount specified" });

        var satsAmount = bolt11.MinimumAmount.ToUnit(LightMoneyUnit.Satoshi);
        var sats = (long)satsAmount;
        if (sats <= 0)
            return BadRequest(new { error = "Invoice amount must be positive" });

        // 2. Convert sats → CAD cents at current exchange rate
        var (cadCents, exchangeRate) = await _exchangeRateService.SatsToCadCentsAsync(sats, wallet!.StoreId);
        if (cadCents <= 0)
            return BadRequest(new { error = "Conversion resulted in zero CAD" });

        // 3. Check sufficient CAD balance
        var balance = await _walletService.GetBalanceAsync(walletId);
        if (balance == null)
            return NotFound(new { error = "Wallet not found" });
        if (balance.CadBalanceCents < cadCents)
            return BadRequest(new { error = "Insufficient CAD balance", required = cadCents, available = balance.CadBalanceCents });

        // 4. Create a payout (claim) against the pull payment with the Lightning invoice
        var lnPayoutMethodId = PayoutMethodId.Parse("BTC-LN");
        var handler = _payoutHandlers.TryGet(lnPayoutMethodId);
        if (handler == null)
            return StatusCode(500, new { error = "Lightning payout handler not available" });

        var (destination, parseError) = await handler.ParseClaimDestination(invoice, CancellationToken.None);
        if (destination == null)
            return BadRequest(new { error = parseError ?? "Could not parse Lightning invoice as claim destination" });

        var claimResult = await _pullPaymentHostedService.Claim(new ClaimRequest
        {
            PullPaymentId = wallet.PullPaymentId,
            PayoutMethodId = lnPayoutMethodId,
            Destination = destination,
            StoreId = wallet.StoreId,
            PreApprove = true,
            Metadata = JObject.FromObject(new { source = "wallet-pay-invoice", walletId = walletId.ToString() })
        });

        if (claimResult.Result != ClaimRequest.ClaimResult.Ok)
        {
            var errorMsg = ClaimRequest.GetErrorMessage(claimResult.Result) ?? "Payout claim failed";
            _logger.LogWarning("Pay-invoice claim failed for wallet {WalletId}: {Result} - {Error}",
                walletId, claimResult.Result, errorMsg);
            return BadRequest(new { error = errorMsg });
        }

        // 5. Deduct CAD balance
        var (spendSuccess, spendError) = await _walletService.SpendCadAsync(
            walletId, cadCents, $"ln-invoice:{bolt11.PaymentHash}");
        if (!spendSuccess)
        {
            _logger.LogError("Pay-invoice: payout created but CAD deduction failed for wallet {WalletId}: {Error}",
                walletId, spendError);
            return StatusCode(500, new { error = "Payment submitted but balance deduction failed: " + spendError });
        }

        // 6. Return success
        var newBalance = await _walletService.GetBalanceAsync(walletId);
        _logger.LogInformation("Pay-invoice: wallet {WalletId} paid {Sats} sats ({CadCents} CAD cents) via Lightning",
            walletId, sats, cadCents);

        return Ok(new
        {
            success = true,
            cadCentsCharged = cadCents,
            satsAmount = sats,
            exchangeRate,
            newCadBalanceCents = newBalance?.CadBalanceCents ?? 0,
            newSatsBalance = newBalance?.SatsBalance ?? 0,
            paymentHash = bolt11.PaymentHash?.ToString()
        });
    }

    // ── LNURL-withdraw claim endpoint ──
    // TODO: Implement claim-lnurl — requires Lightning client access which needs
    // a different approach (Greenfield API or service layer) since plugin can't
    // directly access BTCPay's internal LightningClientFactoryService.
    // For now, LNURL-withdraw earning works via the existing bolt card tap flow.

    // ── NFC tap endpoint ──

    [HttpPost("plugins/bitcoin-rewards/wallet/tap")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Tap([FromBody] WalletTapRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.P) || string.IsNullOrWhiteSpace(request.C))
            return BadRequest(new { error = "Missing p and c parameters" });

        var cardResult = await _boltCardService.FindCardByEncryptedParamsAsync(request.P, request.C);
        if (!cardResult.Success)
            return BadRequest(new { error = cardResult.Error });

        var cardUid = cardResult.Uid != null ? Encoders.Hex.EncodeData(cardResult.Uid) : null;

        // Find the store for this pull payment — look up in BoltCardLinks first
        var link = await _boltCardService.FindByPullPaymentIdAsync(cardResult.PullPaymentId!);
        if (link == null)
            return BadRequest(new { error = "Card not associated with a store" });

        var wallet = await _walletService.GetOrCreateWalletAsync(
            link.StoreId, cardResult.PullPaymentId!, cardUid, cardResult.BoltcardId);

        // Generate or regenerate token on each tap
        var token = await _walletService.GenerateWalletTokenAsync(wallet.Id);

        var balance = await _walletService.GetBalanceAsync(wallet.Id);

        return Ok(new
        {
            walletId = wallet.Id,
            token,
            satsBalance = balance?.SatsBalance ?? 0,
            cadBalanceCents = balance?.CadBalanceCents ?? 0,
            autoConvert = wallet.AutoConvertToCad,
            totalRewardedSats = balance?.TotalRewardedSatoshis ?? 0,
            totalRewardedCadCents = balance?.TotalRewardedCadCents ?? 0
        });
    }

    // ── Admin endpoints (BTCPay auth) ──

    [HttpGet("plugins/bitcoin-rewards/{storeId}/wallets")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie + "," + AuthenticationSchemes.Greenfield,
        Policy = Policies.CanViewStoreSettings)]
    public async Task<IActionResult> ListWallets(string storeId)
    {
        var wallets = await _walletService.GetStoreWalletsAsync(storeId);
        return Ok(wallets.Select(w => new
        {
            id = w.Id,
            pullPaymentId = w.PullPaymentId,
            cardUid = w.CardUid,
            boltcardId = w.BoltcardId,
            cadBalanceCents = w.CadBalanceCents,
            autoConvertToCad = w.AutoConvertToCad,
            totalRewardedSatoshis = w.TotalRewardedSatoshis,
            totalRewardedCadCents = w.TotalRewardedCadCents,
            isActive = w.IsActive,
            createdAt = w.CreatedAt,
            lastRewardedAt = w.LastRewardedAt
        }));
    }

    [HttpPost("plugins/bitcoin-rewards/{storeId}/wallets/{walletId}/adjust")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie + "," + AuthenticationSchemes.Greenfield,
        Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> Adjust(string storeId, Guid walletId, [FromBody] AdjustRequest request)
    {
        var wallet = await _walletService.FindByIdAsync(walletId);
        if (wallet == null || wallet.StoreId != storeId)
            return NotFound(new { error = "Wallet not found" });

        var ok = await _walletService.AdjustAsync(walletId, request.SatsAmount, request.CadCentsAmount, request.Reason);
        if (!ok) return StatusCode(500, new { error = "Adjustment failed" });

        return Ok(new { success = true });
    }

    [HttpPost("plugins/bitcoin-rewards/{storeId}/wallets/{walletId}/spend-cad")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie + "," + AuthenticationSchemes.Greenfield,
        Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> SpendCad(string storeId, Guid walletId, [FromBody] SpendCadRequest request)
    {
        var wallet = await _walletService.FindByIdAsync(walletId);
        if (wallet == null || wallet.StoreId != storeId)
            return NotFound(new { error = "Wallet not found" });

        var (success, error) = await _walletService.SpendCadAsync(walletId, request.AmountCents, request.Reference);
        if (!success)
            return BadRequest(new { error });

        return Ok(new { success = true, newCadBalanceCents = (await _walletService.GetBalanceAsync(walletId))?.CadBalanceCents ?? 0 });
    }

    // ── DTOs ──

    public class SwapRequest
    {
        /// <summary>"to_cad" or "to_sats"</summary>
        public string Direction { get; set; } = string.Empty;
        /// <summary>Amount in sats (if to_cad) or CAD cents (if to_sats)</summary>
        public long Amount { get; set; }
    }

    public class WalletSettingsRequest
    {
        public bool AutoConvert { get; set; }
    }

    public class WalletTapRequest
    {
        public string P { get; set; } = string.Empty;
        public string C { get; set; } = string.Empty;
    }

    public class AdjustRequest
    {
        public long SatsAmount { get; set; }
        public long CadCentsAmount { get; set; }
        public string? Reason { get; set; }
    }

    public class SpendCadRequest
    {
        public long AmountCents { get; set; }
        public string? Reference { get; set; }
    }

    public class PayInvoiceRequest
    {
        public string Invoice { get; set; } = string.Empty;
    }

}
