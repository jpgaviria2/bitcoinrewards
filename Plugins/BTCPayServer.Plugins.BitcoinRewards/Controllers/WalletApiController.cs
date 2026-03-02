#nullable enable
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Web;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Configuration;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly PaymentMethodHandlerDictionary _paymentHandlers;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly StoreRepository _storeRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LightningClientFactoryService _lightningClientFactory;
    private readonly IOptions<LightningNetworkOptions> _lightningOptions;
    private readonly BitcoinRewardsPluginDbContextFactory _dbFactory;
    private readonly ILogger<WalletApiController> _logger;

    public WalletApiController(
        CustomerWalletService walletService,
        BoltCardRewardService boltCardService,
        ExchangeRateService exchangeRateService,
        PullPaymentHostedService pullPaymentHostedService,
        PayoutMethodHandlerDictionary payoutHandlers,
        PaymentMethodHandlerDictionary paymentHandlers,
        BTCPayNetworkProvider networkProvider,
        StoreRepository storeRepository,
        IHttpClientFactory httpClientFactory,
        LightningClientFactoryService lightningClientFactory,
        IOptions<LightningNetworkOptions> lightningOptions,
        BitcoinRewardsPluginDbContextFactory dbFactory,
        ILogger<WalletApiController> logger)
    {
        _walletService = walletService;
        _boltCardService = boltCardService;
        _exchangeRateService = exchangeRateService;
        _pullPaymentHostedService = pullPaymentHostedService;
        _payoutHandlers = payoutHandlers;
        _paymentHandlers = paymentHandlers;
        _networkProvider = networkProvider;
        _storeRepository = storeRepository;
        _httpClientFactory = httpClientFactory;
        _lightningClientFactory = lightningClientFactory;
        _lightningOptions = lightningOptions;
        _dbFactory = dbFactory;
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

        // 2. Check balance — try sats first, then CAD
        var balance = await _walletService.GetBalanceAsync(walletId);
        if (balance == null)
            return NotFound(new { error = "Wallet not found" });

        bool payFromSats = balance.SatsBalance >= sats;
        long cadCents = 0;
        decimal exchangeRate = 0;

        if (!payFromSats)
        {
            // Not enough sats — convert from CAD
            (cadCents, exchangeRate) = await _exchangeRateService.SatsToCadCentsAsync(sats, wallet!.StoreId);
            if (cadCents <= 0)
                return BadRequest(new { error = "Conversion resulted in zero CAD" });
            if (balance.CadBalanceCents < cadCents)
                return BadRequest(new { error = "Insufficient balance", requiredCadCents = cadCents, availableCadCents = balance.CadBalanceCents, availableSats = balance.SatsBalance });
        }

        // 4. Get Lightning client and pay the invoice directly
        var store = await _storeRepository.FindStore(wallet!.StoreId);
        if (store == null)
            return StatusCode(500, new { error = "Store not found" });

        var lnConfig = _paymentHandlers.GetLightningConfig(store, network);
        if (lnConfig == null)
            return StatusCode(500, new { error = "Lightning not configured for this store" });

        ILightningClient? lightningClient = null;
        var connStr = lnConfig.GetExternalLightningUrl();
        if (!string.IsNullOrEmpty(connStr))
        {
            lightningClient = _lightningClientFactory.Create(connStr, network);
        }
        else if (lnConfig.IsInternalNode &&
                 _lightningOptions.Value.InternalLightningByCryptoCode.TryGetValue("BTC", out var internalClient))
        {
            lightningClient = internalClient;
        }

        if (lightningClient == null)
            return StatusCode(500, new { error = "No Lightning connection configured for this store" });

        // 5. Pay the invoice with a timeout
        using var payCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        PayResponse payResult;
        try
        {
            payResult = await lightningClient.Pay(invoice, payCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Pay-invoice: Lightning payment timed out for wallet {WalletId}", walletId);
            return StatusCode(504, new { error = "Lightning payment timed out. No balance was deducted." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pay-invoice: Lightning payment failed for wallet {WalletId}", walletId);
            return StatusCode(502, new { error = $"Lightning payment failed: {ex.Message}" });
        }

        if (payResult.Result != PayResult.Ok)
        {
            _logger.LogWarning("Pay-invoice: Lightning payment not OK for wallet {WalletId}: {Result} {Details}",
                walletId, payResult.Result, payResult.ErrorDetail);
            return BadRequest(new { error = $"Lightning payment failed: {payResult.Result} - {payResult.ErrorDetail}" });
        }

        // 6. Payment confirmed — deduct from appropriate balance
        var reference = $"ln-invoice:{bolt11.PaymentHash}";

        if (payFromSats)
        {
            // Deduct from sats balance (pull payment)
            var (spendSuccess, spendError) = await _walletService.SpendSatsAsync(walletId, sats, reference);
            if (!spendSuccess)
            {
                _logger.LogError("Pay-invoice: payment sent but sats deduction failed for wallet {WalletId}: {Error}. MANUAL RECONCILIATION NEEDED.",
                    walletId, spendError);
                return StatusCode(500, new { error = "Payment sent but balance deduction failed — contact support" });
            }
            _logger.LogInformation("Pay-invoice: wallet {WalletId} paid {Sats} sats from sats balance via Lightning", walletId, sats);
        }
        else
        {
            // Deduct from CAD balance
            var (spendSuccess, spendError) = await _walletService.SpendCadAsync(walletId, cadCents, reference);
            if (!spendSuccess)
            {
                _logger.LogError("Pay-invoice: payment sent but CAD deduction failed for wallet {WalletId}: {Error}. MANUAL RECONCILIATION NEEDED.",
                    walletId, spendError);
                return StatusCode(500, new { error = "Payment sent but balance deduction failed — contact support" });
            }
            _logger.LogInformation("Pay-invoice: wallet {WalletId} paid {Sats} sats ({CadCents} CAD cents) from CAD balance via Lightning",
                walletId, sats, cadCents);
        }

        // 7. Return success
        var newBalance = await _walletService.GetBalanceAsync(walletId);

        return Ok(new
        {
            success = true,
            paidFromSats = payFromSats,
            cadCentsCharged = payFromSats ? 0 : cadCents,
            satsAmount = sats,
            exchangeRate,
            newCadBalanceCents = newBalance?.CadBalanceCents ?? 0,
            newSatsBalance = newBalance?.SatsBalance ?? 0,
            paymentHash = bolt11.PaymentHash?.ToString()
        });
    }

    // ── LNURL-withdraw claim endpoint ──

    public class ClaimLnurlRequest
    {
        public string Callback { get; set; } = "";
        public string K1 { get; set; } = "";
        public long Amount { get; set; } // millisatoshis
        public string? Description { get; set; }
    }

    [HttpPost("plugins/bitcoin-rewards/wallet/{walletId}/claim-lnurl")]
    [AllowAnonymous]
    public async Task<IActionResult> ClaimLnurl(Guid walletId, [FromBody] ClaimLnurlRequest request)
    {
        var (wallet, err) = await AuthAndFindWalletAsync(walletId);
        if (err != null) return err;

        if (string.IsNullOrWhiteSpace(request.Callback) || string.IsNullOrWhiteSpace(request.K1))
            return BadRequest(new { error = "callback and k1 are required" });
        if (request.Amount <= 0)
            return BadRequest(new { error = "amount must be positive (in millisatoshis)" });

        var sats = request.Amount / 1000;
        if (sats <= 0) sats = 1;

        try
        {
            // 1. Get the store's Lightning client
            var store = await _storeRepository.FindStore(wallet!.StoreId);
            if (store == null)
                return StatusCode(500, new { error = "Store not found" });

            var network = _networkProvider.GetNetwork<BTCPayNetwork>("BTC");
            if (network == null)
                return StatusCode(500, new { error = "BTC network not configured" });

            var lnConfig = _paymentHandlers.GetLightningConfig(store, network);
            if (lnConfig == null)
                return StatusCode(500, new { error = "Lightning not configured for this store" });

            ILightningClient? lightningClient = null;
            var connStr = lnConfig.GetExternalLightningUrl();
            if (!string.IsNullOrEmpty(connStr))
            {
                lightningClient = _lightningClientFactory.Create(connStr, network);
            }
            else if (lnConfig.IsInternalNode &&
                     _lightningOptions.Value.InternalLightningByCryptoCode.TryGetValue("BTC", out var internalClient))
            {
                lightningClient = internalClient;
            }

            if (lightningClient == null)
                return StatusCode(500, new { error = "No Lightning connection configured for this store" });

            // 2. Create a Lightning invoice for the claim amount
            var lnInvoice = await lightningClient.CreateInvoice(
                new LightMoney(sats, LightMoneyUnit.Satoshi),
                request.Description ?? "LNURL-withdraw claim",
                TimeSpan.FromMinutes(10),
                CancellationToken.None);

            if (lnInvoice == null || string.IsNullOrEmpty(lnInvoice.BOLT11))
            {
                _logger.LogError("Failed to create Lightning invoice for LNURL claim, wallet {WalletId}", walletId);
                return StatusCode(500, new { error = "Failed to create Lightning invoice" });
            }

            // 3. Send the invoice to the LNURL-withdraw callback
            var callbackUri = new UriBuilder(request.Callback);
            var query = HttpUtility.ParseQueryString(callbackUri.Query);
            query["k1"] = request.K1;
            query["pr"] = lnInvoice.BOLT11;
            callbackUri.Query = query.ToString();

            var httpClient = _httpClientFactory.CreateClient("lnurl-withdraw");
            var callbackResponse = await httpClient.GetAsync(callbackUri.Uri);
            var callbackBody = await callbackResponse.Content.ReadAsStringAsync();

            if (!callbackResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("LNURL callback failed for wallet {WalletId}: {Status} {Body}",
                    walletId, callbackResponse.StatusCode, callbackBody);
                return BadRequest(new { error = $"LNURL callback failed: {callbackBody}" });
            }

            // Parse callback response — should be {"status":"OK"} per LUD-03
            using var jsonDoc = JsonDocument.Parse(callbackBody);
            var status = jsonDoc.RootElement.TryGetProperty("status", out var statusProp)
                ? statusProp.GetString()
                : null;

            if (status != null && status.Equals("ERROR", StringComparison.OrdinalIgnoreCase))
            {
                var reason = jsonDoc.RootElement.TryGetProperty("reason", out var reasonProp)
                    ? reasonProp.GetString()
                    : "Unknown error";
                return BadRequest(new { error = $"LNURL service rejected claim: {reason}" });
            }

            // 4. LNURL callback returned OK — the sender has committed to paying.
            //    Per LUD-03, status:OK means the service accepted our invoice and
            //    will pay it. We trust this and credit optimistically.
            //    If auto-convert is ON: convert sats → CAD and credit CadBalance.
            //    If auto-convert is OFF: leave sats as sats (no CAD conversion).
            long cadCents = 0;
            decimal exchangeRate = 0;
            var reference = $"lnurl-withdraw:{request.K1[..Math.Min(8, request.K1.Length)]}";

            if (wallet.AutoConvertToCad)
            {
                (cadCents, exchangeRate) = await _exchangeRateService.SatsToCadCentsAsync(sats, wallet.StoreId);
                await _walletService.CreditCadAsync(walletId, cadCents, sats, exchangeRate, reference);
                _logger.LogInformation("LNURL-withdraw claimed (auto-convert ON): wallet {WalletId} credited {Sats} sats → {CadCents} CAD cents @ {Rate}",
                    walletId, sats, cadCents, exchangeRate);
            }
            else
            {
                await _walletService.CreditSatsAsync(walletId, sats, reference);
                _logger.LogInformation("LNURL-withdraw claimed (auto-convert OFF): wallet {WalletId} credited {Sats} sats (kept as sats)",
                    walletId, sats);
            }

            var newBalance = await _walletService.GetBalanceAsync(walletId);

            return Ok(new
            {
                success = true,
                pending = false,
                satsReceived = sats,
                autoConverted = wallet.AutoConvertToCad,
                cadCentsConverted = cadCents,
                exchangeRate,
                newCadBalanceCents = newBalance?.CadBalanceCents ?? 0,
                newSatsBalance = newBalance?.SatsBalance ?? 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LNURL claim failed for wallet {WalletId}", walletId);
            return StatusCode(500, new { error = "Claim failed: " + ex.Message });
        }
    }

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

    public class CreateWalletRequest
    {
        public string? StoreId { get; set; }
    }

    // ── Anonymous wallet creation (frictionless onboarding) ──

    [HttpPost("plugins/bitcoin-rewards/wallet/create")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CreateWallet([FromBody] CreateWalletRequest? request)
    {
        // Resolve store ID: from request body, or fall back to finding a store with the plugin enabled
        var storeId = request?.StoreId;

        if (string.IsNullOrWhiteSpace(storeId))
        {
            // No storeId provided — this is expected for the simple "Get Started" flow.
            // We need at least one store with the plugin enabled.
            // For now, return an error asking for storeId. In production, you'd configure a default.
            return BadRequest(new { error = "storeId is required" });
        }

        // Verify store exists
        var store = await _storeRepository.FindStore(storeId);
        if (store == null)
            return BadRequest(new { error = "Store not found" });

        // Rate limiting: max 100 wallets per store per day
        var todayCount = await _walletService.CountWalletsCreatedTodayAsync(storeId);
        if (todayCount >= 100)
        {
            _logger.LogWarning("Wallet creation rate limit hit for store {StoreId}: {Count} today", storeId, todayCount);
            return StatusCode(429, new { error = "Too many wallets created today. Try again tomorrow." });
        }

        try
        {
            // Create a pull payment with 0 initial balance (SATS currency, LN payout method)
            var ppRequest = new BTCPayServer.Client.Models.CreatePullPaymentRequest
            {
                Name = "Rewards Wallet",
                Description = "Trails Coffee customer rewards wallet",
                Amount = 1.0m, // 1 BTC limit — pull payment is just a container, actual balance tracked in DB
                Currency = "BTC",
                AutoApproveClaims = true,
                StartsAt = DateTimeOffset.UtcNow,
                PayoutMethods = new[] { "BTC-LN" }
            };

            var pullPaymentId = await _pullPaymentHostedService.CreatePullPayment(store, ppRequest);
            if (string.IsNullOrEmpty(pullPaymentId))
            {
                _logger.LogError("Failed to create pull payment for anonymous wallet in store {StoreId}", storeId);
                return StatusCode(500, new { error = "Failed to create wallet backing" });
            }

            // Create CustomerWallet linked to the pull payment
            var wallet = await _walletService.GetOrCreateWalletAsync(storeId, pullPaymentId);

            // Generate bearer token
            var token = await _walletService.GenerateWalletTokenAsync(wallet.Id);
            if (token == null)
            {
                _logger.LogError("Failed to generate token for wallet {WalletId}", wallet.Id);
                return StatusCode(500, new { error = "Failed to generate wallet token" });
            }

            _logger.LogInformation("Created anonymous wallet {WalletId} for store {StoreId}, PP {PullPaymentId}",
                wallet.Id, storeId, pullPaymentId);

            return Ok(new
            {
                walletId = wallet.Id,
                token,
                satsBalance = 0,
                cadBalanceCents = 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating anonymous wallet for store {StoreId}", storeId);
            return StatusCode(500, new { error = "Internal error creating wallet" });
        }
    }
}
