#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin.DataEncoders;

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
    private readonly ILogger<WalletApiController> _logger;

    public WalletApiController(
        CustomerWalletService walletService,
        BoltCardRewardService boltCardService,
        ExchangeRateService exchangeRateService,
        ILogger<WalletApiController> logger)
    {
        _walletService = walletService;
        _boltCardService = boltCardService;
        _exchangeRateService = exchangeRateService;
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
}
