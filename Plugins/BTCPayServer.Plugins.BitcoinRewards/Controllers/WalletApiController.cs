#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;
using NicolasDorier.RateLimits;

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
    private readonly PaymentMethodHandlerDictionary _paymentHandlers;
    private readonly StoreRepository _storeRepository;
    private readonly BitcoinRewardsPluginDbContextFactory _dbFactory;
    private readonly Services.IdempotencyService _idempotencyService;
    private readonly Services.Nip05Service _nip05Service;
    private readonly ILogger<WalletApiController> _logger;

    public WalletApiController(
        CustomerWalletService walletService,
        BoltCardRewardService boltCardService,
        ExchangeRateService exchangeRateService,
        PullPaymentHostedService pullPaymentHostedService,
        PayoutMethodHandlerDictionary payoutHandlers,
        BTCPayNetworkProvider networkProvider,
        PaymentMethodHandlerDictionary paymentHandlers,
        StoreRepository storeRepository,
        BitcoinRewardsPluginDbContextFactory dbFactory,
        Services.IdempotencyService idempotencyService,
        Services.Nip05Service nip05Service,
        ILogger<WalletApiController> logger)
    {
        _walletService = walletService;
        _boltCardService = boltCardService;
        _exchangeRateService = exchangeRateService;
        _pullPaymentHostedService = pullPaymentHostedService;
        _payoutHandlers = payoutHandlers;
        _networkProvider = networkProvider;
        _paymentHandlers = paymentHandlers;
        _storeRepository = storeRepository;
        _dbFactory = dbFactory;
        _idempotencyService = idempotencyService;
        _nip05Service = nip05Service;
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

    [HttpPost("plugins/bitcoin-rewards/wallet/create")]
    [AllowAnonymous]
    [RateLimitsFilter(ZoneLimits.Register, Scope = RateLimitsScope.RemoteAddress)]
    public async Task<IActionResult> CreateWallet([FromBody] CreateWalletRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.StoreId))
            return BadRequest(new { error = "StoreId is required" });

        try
        {
            // Get store
            var store = await _storeRepository.FindStore(request.StoreId);
            if (store == null)
                return BadRequest(new { error = "Store not found" });

            // Create a pull payment for this wallet with 1 BTC limit (100M sats)
            var network = _networkProvider.GetNetwork<BTCPayNetwork>("BTC");
            var lnPayoutMethodId = PayoutMethodId.Parse("BTC-LN");
            
            var pullPaymentRequest = new CreatePullPaymentRequest
            {
                Name = "Wallet Pull Payment",
                Description = "Rewards wallet",
                Amount = 1.0m, // 1 BTC = 100,000,000 sats
                Currency = "BTC",
                AutoApproveClaims = true,
                StartsAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddYears(10),
                PayoutMethods = new[] { lnPayoutMethodId.ToString() }
            };

            var pullPaymentId = await _pullPaymentHostedService.CreatePullPayment(store, pullPaymentRequest);
            if (string.IsNullOrEmpty(pullPaymentId))
                return StatusCode(500, new { error = "Failed to create pull payment" });

            // Create wallet
            var wallet = await _walletService.GetOrCreateWalletAsync(
                request.StoreId, pullPaymentId, cardUid: null, boltcardId: null);

            // Handle NIP-05: pubkey + username
            string? nip05 = null;
            if (!string.IsNullOrWhiteSpace(request.Pubkey))
            {
                // Check pubkey not already registered
                if (await _nip05Service.IsPubkeyRegistered(request.Pubkey))
                    return BadRequest(new { error = "Pubkey already registered" });

                // Validate or auto-generate username
                string username;
                if (!string.IsNullOrWhiteSpace(request.Username))
                {
                    var lower = request.Username.ToLowerInvariant();
                    var (valid, validError) = _nip05Service.ValidateUsername(lower);
                    if (!valid)
                        return BadRequest(new { error = validError });
                    if (!await _nip05Service.IsUsernameAvailable(lower))
                        return BadRequest(new { error = "Username already taken" });
                    username = lower;
                }
                else
                {
                    username = await _nip05Service.GenerateUsername();
                }

                // Store in wallet record
                await _nip05Service.SetWalletNip05(wallet.Id, request.Pubkey, username);
                nip05 = $"{username}@trailscoffee.com";
            }

            // Generate token
            var token = await _walletService.GenerateWalletTokenAsync(wallet.Id);

            var balance = await _walletService.GetBalanceAsync(wallet.Id);

            // Build lud16 (Lightning Address) if user has a username
            string? lud16 = null;
            if (!string.IsNullOrWhiteSpace(request.Username))
            {
                var host = Request.Host.Value;
                lud16 = $"{request.Username.ToLowerInvariant()}@{host}";
            }
            else if (!string.IsNullOrWhiteSpace(request.Pubkey))
            {
                // Auto-generated username case
                var nip05Identity = await _nip05Service.LookupByPubkey(request.Pubkey);
                if (nip05Identity.identity != null)
                {
                    var host = Request.Host.Value;
                    lud16 = $"{nip05Identity.identity.Username}@{host}";
                }
            }

            return Ok(new
            {
                walletId = wallet.Id,
                token,
                satsBalance = balance?.SatsBalance ?? 0,
                cadBalanceCents = balance?.CadBalanceCents ?? 0,
                autoConvert = balance?.AutoConvertToCad ?? true,
                nip05,
                lud16
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create wallet for store {StoreId}", request.StoreId);
            return StatusCode(500, new { error = "Failed to create wallet: " + ex.Message });
        }
    }

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

        // IDEMPOTENCY: Generate key from wallet + invoice hash if not provided
        var invoice = request.Invoice.Trim();
        if (invoice.StartsWith("lightning:", StringComparison.OrdinalIgnoreCase))
            invoice = invoice["lightning:".Length..];
        
        var idempotencyKey = request.IdempotencyKey 
            ?? _idempotencyService.GenerateKey(walletId, "pay-invoice", invoice);
        
        // Check for duplicate request
        var cachedResult = _idempotencyService.GetCachedResult<object>(idempotencyKey);
        if (cachedResult != null)
        {
            _logger.LogInformation("Returning cached payment result for wallet {WalletId}, idempotency key {Key}", 
                walletId, idempotencyKey);
            return Ok(cachedResult);
        }

        // Invoice already trimmed and processed in idempotency section above

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

        // 4. Get Lightning client and pay the invoice SYNCHRONOUSLY
        var lightningClient = await GetLightningClientAsync(wallet.StoreId, network);
        if (lightningClient == null)
            return StatusCode(500, new { error = "Lightning node not configured for this store" });

        PayResponse? paymentResult = null;
        try
        {
            // CRITICAL: Actually pay the invoice and wait for result (with 60s timeout)
            var payTask = lightningClient.Pay(invoice);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(60));
            var completedTask = await Task.WhenAny(payTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("Pay-invoice: Lightning payment timeout for wallet {WalletId} after 60s", walletId);
                return StatusCode(504, new { error = "Lightning payment timeout after 60 seconds" });
            }
            
            paymentResult = await payTask;
            
            if (paymentResult == null)
            {
                _logger.LogError("Pay-invoice: Lightning client returned null for wallet {WalletId}", walletId);
                return StatusCode(500, new { error = "Lightning payment failed: no response from node" });
            }

            // Check payment result
            if (paymentResult.Result != PayResult.Ok)
            {
                var errorDetail = paymentResult.ErrorDetail ?? paymentResult.Result.ToString();
                _logger.LogWarning("Pay-invoice: Lightning payment failed for wallet {WalletId}: {Result} {Detail}",
                    walletId, paymentResult.Result, errorDetail);
                
                return BadRequest(new 
                { 
                    error = $"Lightning payment failed: {paymentResult.Result}", 
                    detail = errorDetail,
                    code = paymentResult.Result.ToString()
                });
            }

            // Payment succeeded!
            _logger.LogInformation("Pay-invoice: Lightning payment OK for wallet {WalletId}, preimage: {Preimage}",
                walletId, paymentResult.Details?.Preimage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pay-invoice: Lightning payment exception for wallet {WalletId}", walletId);
            return StatusCode(500, new { error = "Lightning payment failed: " + ex.Message });
        }

        // 5. Payment confirmed - now deduct CAD balance
        var (spendSuccess, spendError) = await _walletService.SpendCadAsync(
            walletId, cadCents, $"ln-invoice:{bolt11.PaymentHash}");
        if (!spendSuccess)
        {
            // CRITICAL: Payment went through but we couldn't deduct CAD
            // This is a data consistency issue - log as error for manual review
            _logger.LogError("Pay-invoice: CRITICAL - Lightning payment succeeded but CAD deduction failed for wallet {WalletId}: {Error}. " +
                "Preimage: {Preimage}, Amount: {CadCents} CAD cents",
                walletId, spendError, paymentResult.Details?.Preimage, cadCents);
            
            // Don't return error to user since payment went through
            // But track in response for debugging
            var bal = await _walletService.GetBalanceAsync(walletId);
            var warningResult = new
            {
                success = true,
                warning = "Payment succeeded but balance update failed - contact support",
                cadCentsCharged = cadCents,
                satsAmount = sats,
                exchangeRate,
                newCadBalanceCents = bal?.CadBalanceCents ?? 0,
                newSatsBalance = bal?.SatsBalance ?? 0,
                paymentHash = bolt11.PaymentHash?.ToString(),
                preimage = paymentResult.Details?.Preimage
            };
            
            // IDEMPOTENCY: Cache this result (even though it's a warning case)
            _idempotencyService.CacheResult(idempotencyKey, warningResult);
            
            return Ok(warningResult);
        }

        // 6. Return success - payment confirmed and balance deducted
        var newBalance = await _walletService.GetBalanceAsync(walletId);
        _logger.LogInformation("Pay-invoice: wallet {WalletId} successfully paid {Sats} sats ({CadCents} CAD cents) via Lightning",
            walletId, sats, cadCents);

        var result = new
        {
            success = true,
            cadCentsCharged = cadCents,
            satsAmount = sats,
            exchangeRate,
            newCadBalanceCents = newBalance?.CadBalanceCents ?? 0,
            newSatsBalance = newBalance?.SatsBalance ?? 0,
            paymentHash = bolt11.PaymentHash?.ToString(),
            preimage = paymentResult.Details?.Preimage
        };
        
        // IDEMPOTENCY: Cache successful result
        _idempotencyService.CacheResult(idempotencyKey, result);
        
        return Ok(result);
    }

    // ── LNURL claim endpoint ──

    [HttpPost("plugins/bitcoin-rewards/wallet/{walletId}/claim-lnurl")]
    [AllowAnonymous]
    public async Task<IActionResult> ClaimLnurl(Guid walletId, [FromBody] ClaimLnurlRequest request)
    {
        var (wallet, err) = await AuthAndFindWalletAsync(walletId);
        if (err != null) return err;

        // Validate request
        if (string.IsNullOrWhiteSpace(request.Callback))
            return BadRequest(new { error = "Callback URL is required" });
        if (string.IsNullOrWhiteSpace(request.K1))
            return BadRequest(new { error = "k1 is required" });
        if (request.Amount <= 0)
            return BadRequest(new { error = "Amount must be positive (in millisats)" });

        try
        {
            // Get Lightning client for the store
            var network = _networkProvider.GetNetwork<BTCPayNetwork>("BTC");
            if (network == null)
                return StatusCode(500, new { error = "BTC network not configured" });

            var lightningClient = await GetLightningClientAsync(wallet!.StoreId, network);
            if (lightningClient == null)
                return StatusCode(500, new { error = "Lightning node not configured for this store" });

            // Create a Lightning invoice
            var description = request.Description ?? "LNURL Withdraw";
            var invoice = await lightningClient.CreateInvoice(
                new LightMoney(request.Amount, LightMoneyUnit.MilliSatoshi),
                description,
                TimeSpan.FromMinutes(10),
                CancellationToken.None);

            if (string.IsNullOrEmpty(invoice.BOLT11))
                return StatusCode(500, new { error = "Lightning node returned invoice without BOLT11" });

            // Save pending claim for the watcher to track
            await using (var db = _dbFactory.CreateContext())
            {
                var sats = request.Amount / 1000;
                var pendingClaim = new PendingLnurlClaim
                {
                    Id = Guid.NewGuid(),
                    CustomerWalletId = walletId,
                    StoreId = wallet.StoreId,
                    LightningInvoiceId = invoice.Id,
                    Bolt11 = invoice.BOLT11,
                    ExpectedSats = sats,
                    K1Prefix = request.K1.Length > 8 ? request.K1[..8] : request.K1,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                    IsCompleted = false,
                    IsFailed = false
                };
                db.PendingLnurlClaims.Add(pendingClaim);
                await db.SaveChangesAsync();
            }

            // Call the LNURL callback with our invoice
            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            
            var separator = request.Callback.Contains('?') ? "&" : "?";
            var callbackUrl = $"{request.Callback}{separator}k1={Uri.EscapeDataString(request.K1)}&pr={Uri.EscapeDataString(invoice.BOLT11)}";

            _logger.LogInformation("Calling LNURL callback for wallet {WalletId}: {Url}", walletId, callbackUrl);

            var response = await httpClient.GetAsync(callbackUrl);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("LNURL callback returned {StatusCode}: {Body}", response.StatusCode, responseBody);
                return BadRequest(new { error = $"LNURL callback failed: {response.StatusCode}", details = responseBody });
            }

            // Check for LNURL error response
            try
            {
                var json = JObject.Parse(responseBody);
                var status = json["status"]?.ToString();
                if (string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    var reason = json["reason"]?.ToString() ?? "Unknown error";
                    _logger.LogWarning("LNURL callback returned error: {Reason}", reason);
                    return BadRequest(new { error = $"LNURL service error: {reason}" });
                }
            }
            catch (Exception)
            {
                // Not JSON or not parseable, assume success
            }

            _logger.LogInformation("LNURL callback successful for wallet {WalletId}", walletId);

            // Return success - the LnurlClaimWatcherService will credit the wallet when payment arrives
            return Ok(new
            {
                success = true,
                message = "Claim submitted. Your balance will update once the Lightning payment settles.",
                invoice = invoice.BOLT11
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process LNURL claim for wallet {WalletId}", walletId);
            return StatusCode(500, new { error = "Failed to process LNURL claim: " + ex.Message });
        }
    }

    // Helper method to get Lightning client for a store
    private async Task<ILightningClient?> GetLightningClientAsync(string storeId, BTCPayNetwork network)
    {
        var store = await _storeRepository.FindStore(storeId);
        if (store == null)
            return null;

        var id = PaymentTypes.LN.GetPaymentMethodId(network.CryptoCode);
        if (!_paymentHandlers.TryGetValue(id, out var handler) || handler is not LightningLikePaymentHandler lnHandler)
            return null;

        var existing = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(id, _paymentHandlers);
        if (existing == null)
            return null;

        return lnHandler.CreateLightningClient(existing);
    }

    // ── Internal transfer endpoint ──

    [HttpPost("plugins/bitcoin-rewards/wallet/transfer")]
    [AllowAnonymous]
    public async Task<IActionResult> Transfer([FromBody] TransferRequest request)
    {
        var wallet = await AuthenticateWalletAsync();
        if (wallet == null)
            return Unauthorized(new { error = "Invalid or missing wallet token" });
        if (wallet.Id != request.FromWalletId)
            return StatusCode(403, new { error = "Token does not match wallet" });

        if (string.IsNullOrWhiteSpace(request.ToUsername))
            return BadRequest(new { error = "toUsername is required" });
        if (request.AmountSats <= 0)
            return BadRequest(new { error = "amountSats must be positive" });

        // Sanitize memo: limit length and strip dangerous characters
        var sanitizedMemo = SanitizeMemo(request.Memo);

        var (success, error, fromSats, fromCadCents) = await _walletService.TransferAsync(
            request.FromWalletId, request.ToUsername, request.AmountSats, sanitizedMemo, wallet.StoreId);

        if (!success)
            return BadRequest(new { error });

        return Ok(new
        {
            success = true,
            fromBalance = new { satsBalance = fromSats ?? 0, cadBalanceCents = fromCadCents ?? 0 },
            toUsername = request.ToUsername,
            amountSats = request.AmountSats
        });
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

    // ── Helpers ──

    /// <summary>Sanitize memo: limit to 255 chars, strip control characters and HTML tags.</summary>
    private static string? SanitizeMemo(string? memo)
    {
        if (string.IsNullOrWhiteSpace(memo)) return null;
        // Strip HTML tags
        memo = System.Text.RegularExpressions.Regex.Replace(memo, "<[^>]*>", "");
        // Strip control characters (except space)
        memo = new string(memo.Where(c => !char.IsControl(c) || c == ' ').ToArray());
        // Trim and limit length
        memo = memo.Trim();
        if (memo.Length > 255) memo = memo[..255];
        return string.IsNullOrWhiteSpace(memo) ? null : memo;
    }

    // ── DTOs ──

    public class CreateWalletRequest
    {
        public string StoreId { get; set; } = string.Empty;
        /// <summary>Optional Nostr public key (hex) for NIP-05 identity.</summary>
        public string? Pubkey { get; set; }
        /// <summary>Optional NIP-05 username. Auto-generated if pubkey provided without username.</summary>
        public string? Username { get; set; }
    }

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

    public class PayInvoiceRequest : Services.IIdempotentRequest
    {
        public string Invoice { get; set; } = string.Empty;
        
        /// <summary>
        /// Optional idempotency key to prevent duplicate payments.
        /// If not provided, server generates one from wallet ID + invoice hash.
        /// </summary>
        public string? IdempotencyKey { get; set; }
    }

    public class TransferRequest
    {
        public Guid FromWalletId { get; set; }
        public string ToUsername { get; set; } = string.Empty;
        public long AmountSats { get; set; }
        public string? Memo { get; set; }
    }

    public class ClaimLnurlRequest
    {
        public string Callback { get; set; } = string.Empty;
        public string K1 { get; set; } = string.Empty;
        public long Amount { get; set; }
        public string? Description { get; set; }
    }
}
