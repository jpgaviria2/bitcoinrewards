#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers;

[ApiController]
public class Nip05ApiController : ControllerBase
{
    private readonly Nip05Service _nip05;
    private readonly CustomerWalletService _walletService;
    private readonly ILogger<Nip05ApiController> _logger;

    public Nip05ApiController(
        Nip05Service nip05,
        CustomerWalletService walletService,
        ILogger<Nip05ApiController> logger)
    {
        _nip05 = nip05;
        _walletService = walletService;
        _logger = logger;
    }

    // ── Helper: wallet token auth ──
    private async Task<Data.CustomerWallet?> AuthenticateWalletAsync()
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
        catch { return null; }
    }

    // ── Helper: admin key auth ──
    private bool IsAdminAuthorized()
    {
        var adminKey = Environment.GetEnvironmentVariable("BTCPAY_ADMIN_API_KEY");
        if (string.IsNullOrEmpty(adminKey)) return false;
        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (auth == null || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;
        return auth["Bearer ".Length..].Trim() == adminKey;
    }

    /// <summary>Check username availability.</summary>
    [HttpGet("plugins/bitcoin-rewards/nip05/check")]
    [AllowAnonymous]
    [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
    public async Task<IActionResult> CheckUsername([FromQuery] string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new CheckUsernameResponse { Available = false, Reason = "Name is required" });

        var lower = name.ToLowerInvariant();
        var (valid, error) = _nip05.ValidateUsername(lower);
        if (!valid)
        {
            var suggestion = await _nip05.GenerateUsername();
            return Ok(new CheckUsernameResponse { Available = false, Reason = error, Suggestion = suggestion });
        }

        var available = await _nip05.IsUsernameAvailable(lower);
        if (!available)
        {
            var suggestion = await _nip05.GenerateUsername();
            return Ok(new CheckUsernameResponse { Available = false, Reason = "Username already taken", Suggestion = suggestion });
        }

        return Ok(new CheckUsernameResponse { Available = true });
    }

    /// <summary>NIP-05 well-known endpoint. Returns nostr.json with CORS.</summary>
    [HttpGet("plugins/bitcoin-rewards/nip05/nostr.json")]
    [AllowAnonymous]
    public async Task<IActionResult> NostrJson([FromQuery] string? name = null)
    {
        Response.Headers["Access-Control-Allow-Origin"] = "*";
        Response.Headers["Access-Control-Allow-Methods"] = "GET";
        Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";

        var names = await _nip05.GetNostrJson(name);
        return Ok(new NostrJsonResponse { Names = names });
    }

    /// <summary>Lookup NIP-05 identity by pubkey.</summary>
    [HttpGet("plugins/bitcoin-rewards/nip05/lookup")]
    [AllowAnonymous]
    public async Task<IActionResult> Lookup([FromQuery] string pubkey)
    {
        if (string.IsNullOrWhiteSpace(pubkey))
            return BadRequest(new { error = "pubkey is required" });

        var (identity, walletId) = await _nip05.LookupByPubkey(pubkey);
        if (identity == null)
            return NotFound(new { error = "No NIP-05 identity found for this pubkey" });

        return Ok(new Nip05LookupResponse
        {
            WalletId = walletId,
            Nip05 = $"{identity.Username}@trailscoffee.com",
            CreatedAt = identity.CreatedAt,
            Revoked = identity.Revoked
        });
    }

    /// <summary>Update username for a wallet (wallet token auth).</summary>
    [HttpPost("plugins/bitcoin-rewards/nip05/update")]
    [AllowAnonymous]
    [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
    public async Task<IActionResult> UpdateUsername([FromBody] UpdateUsernameRequest request)
    {
        var wallet = await AuthenticateWalletAsync();
        if (wallet == null)
            return Unauthorized(new { error = "Invalid or missing wallet token" });
        if (wallet.Id != request.WalletId)
            return StatusCode(403, new { error = "Token does not match wallet" });

        var lower = request.NewUsername.ToLowerInvariant();
        var (valid, error) = _nip05.ValidateUsername(lower);
        if (!valid)
            return BadRequest(new { error });

        var available = await _nip05.IsUsernameAvailable(lower);
        if (!available)
            return BadRequest(new { error = "Username already taken" });

        await _nip05.UpdateWalletUsername(wallet.Id, lower);

        _logger.LogInformation("Updated NIP-05 username for wallet {WalletId} to {Username}", wallet.Id, lower);
        return Ok(new { success = true, nip05 = $"{lower}@trailscoffee.com" });
    }

    /// <summary>Admin: revoke a NIP-05 identity.</summary>
    [HttpPost("plugins/bitcoin-rewards/nip05/revoke")]
    [AllowAnonymous]
    public async Task<IActionResult> Revoke([FromBody] RevokeRestoreRequest request)
    {
        if (!IsAdminAuthorized())
            return Unauthorized(new { error = "Admin API key required" });

        await _nip05.RevokeNip05(request.Pubkey);
        return Ok(new { success = true, message = $"Revoked NIP-05 for {request.Pubkey}" });
    }

    /// <summary>Admin: restore a revoked NIP-05 identity.</summary>
    [HttpPost("plugins/bitcoin-rewards/nip05/restore")]
    [AllowAnonymous]
    public async Task<IActionResult> Restore([FromBody] RevokeRestoreRequest request)
    {
        if (!IsAdminAuthorized())
            return Unauthorized(new { error = "Admin API key required" });

        await _nip05.RestoreNip05(request.Pubkey);
        return Ok(new { success = true, message = $"Restored NIP-05 for {request.Pubkey}" });
    }

    /// <summary>Admin: list all NIP-05 identities.</summary>
    [HttpGet("plugins/bitcoin-rewards/nip05/list")]
    [AllowAnonymous]
    public async Task<IActionResult> ListAll()
    {
        if (!IsAdminAuthorized())
            return Unauthorized(new { error = "Admin API key required" });

        var users = await _nip05.ListAll();
        return Ok(users);
    }
}
