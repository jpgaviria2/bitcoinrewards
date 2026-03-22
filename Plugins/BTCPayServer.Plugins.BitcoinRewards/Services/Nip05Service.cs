#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

public class Nip05Service
{
    private readonly BitcoinRewardsPluginDbContextFactory _dbFactory;
    private readonly OffensiveWordFilter _filter;
    private readonly ILogger<Nip05Service> _logger;
    private static readonly Regex UsernameRegex = new("^[a-z0-9-]{3,20}$", RegexOptions.Compiled);

    public Nip05Service(
        BitcoinRewardsPluginDbContextFactory dbFactory,
        OffensiveWordFilter filter,
        ILogger<Nip05Service> logger)
    {
        _dbFactory = dbFactory;
        _filter = filter;
        _logger = logger;
    }

    public bool ValidateUsernameFormat(string username)
        => UsernameRegex.IsMatch(username);

    public async Task<bool> IsUsernameAvailable(string username)
    {
        var lower = username.ToLowerInvariant();
        await using var db = _dbFactory.CreateContext();
        
        var inWallets = await db.CustomerWallets.AnyAsync(w => w.Nip05Username == lower);
        if (inWallets) return false;
        
        // Check standalone identities — revoked+released ones in cooldown are unavailable
        var identity = await db.Nip05Identities.FirstOrDefaultAsync(i => i.Username == lower);
        if (identity == null) return true;
        if (!identity.Revoked) return false; // active identity
        // Revoked with ReleasedAt — check cooldown
        if (identity.ReleasedAt.HasValue)
        {
            var cooldownEnd = identity.ReleasedAt.Value.AddDays(7);
            if (DateTime.UtcNow < cooldownEnd) return false; // still in cooldown
            // Cooldown expired — username is free, clean up the identity
            return true;
        }
        // Revoked by admin (no ReleasedAt) — still unavailable
        return false;
    }

    /// <summary>Check username availability with cooldown info for /nip05/check.</summary>
    public async Task<(bool available, string? reason, DateTime? availableAfter)> CheckUsernameAvailability(string username)
    {
        var lower = username.ToLowerInvariant();
        await using var db = _dbFactory.CreateContext();
        
        var inWallets = await db.CustomerWallets.AnyAsync(w => w.Nip05Username == lower);
        if (inWallets) return (false, "Username already taken", null);
        
        var identity = await db.Nip05Identities.FirstOrDefaultAsync(i => i.Username == lower);
        if (identity == null) return (true, null, null);
        if (!identity.Revoked) return (false, "Username already taken", null);
        if (identity.ReleasedAt.HasValue)
        {
            var cooldownEnd = identity.ReleasedAt.Value.AddDays(7);
            if (DateTime.UtcNow < cooldownEnd)
                return (false, $"Recently released, available after {cooldownEnd:yyyy-MM-dd}", cooldownEnd);
            return (true, null, null); // cooldown expired
        }
        return (false, "Username already taken", null); // admin-revoked
    }

    public async Task<string> GenerateUsername()
    {
        for (int i = 0; i < 20; i++)
        {
            var hex = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
            var candidate = $"coffeelover{hex}";
            if (await IsUsernameAvailable(candidate))
                return candidate;
        }
        // Fallback with more entropy
        return $"coffeelover{Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}";
    }

    public (bool Valid, string? Error) ValidateUsername(string username)
    {
        if (!ValidateUsernameFormat(username))
            return (false, "Username must be 3-20 characters, lowercase alphanumeric or hyphens");
        
        var (offensive, reason) = _filter.Check(username);
        if (offensive)
            return (false, reason);
        
        return (true, null);
    }

    public async Task<Dictionary<string, string>> GetNostrJson(string? name = null)
    {
        await using var db = _dbFactory.CreateContext();
        var names = new Dictionary<string, string>();

        if (name != null)
        {
            var lower = name.ToLowerInvariant();
            // Check wallets first
            var wallet = await db.CustomerWallets
                .Where(w => w.Nip05Username == lower && !w.Nip05Revoked && w.Pubkey != null)
                .Select(w => new { w.Nip05Username, w.Pubkey })
                .FirstOrDefaultAsync();
            if (wallet != null)
            {
                names[wallet.Nip05Username!] = wallet.Pubkey!;
                return names;
            }
            // Check standalone identities
            var identity = await db.Nip05Identities
                .Where(i => i.Username == lower && !i.Revoked)
                .FirstOrDefaultAsync();
            if (identity != null)
                names[identity.Username] = identity.Pubkey;
            return names;
        }

        // Return all non-revoked
        var wallets = await db.CustomerWallets
            .Where(w => w.Nip05Username != null && !w.Nip05Revoked && w.Pubkey != null)
            .Select(w => new { w.Nip05Username, w.Pubkey })
            .ToListAsync();
        foreach (var w in wallets)
            names[w.Nip05Username!] = w.Pubkey!;

        var identities = await db.Nip05Identities
            .Where(i => !i.Revoked)
            .ToListAsync();
        foreach (var i in identities)
            names[i.Username] = i.Pubkey;

        return names;
    }

    public async Task<(Nip05Identity? identity, Guid? walletId)> LookupByPubkey(string pubkey)
    {
        await using var db = _dbFactory.CreateContext();
        // Check standalone first
        var identity = await db.Nip05Identities.FirstOrDefaultAsync(i => i.Pubkey == pubkey);
        if (identity != null) return (identity, null);

        // Check wallets
        var wallet = await db.CustomerWallets
            .Where(w => w.Pubkey == pubkey && w.Nip05Username != null)
            .FirstOrDefaultAsync();
        if (wallet != null)
        {
            var walletIdentity = new Nip05Identity
            {
                Pubkey = wallet.Pubkey!,
                Username = wallet.Nip05Username!,
                Revoked = wallet.Nip05Revoked,
                CreatedAt = wallet.CreatedAt
            };
            return (walletIdentity, wallet.Id);
        }
        return (null, null);
    }

    public async Task RevokeNip05(string pubkey)
    {
        await using var db = _dbFactory.CreateContext();
        var identity = await db.Nip05Identities.FirstOrDefaultAsync(i => i.Pubkey == pubkey);
        if (identity != null) { identity.Revoked = true; }
        
        var wallet = await db.CustomerWallets.FirstOrDefaultAsync(w => w.Pubkey == pubkey);
        if (wallet != null) { wallet.Nip05Revoked = true; }
        
        await db.SaveChangesAsync();
        _logger.LogInformation("Revoked NIP-05 for pubkey {Pubkey}", pubkey);
    }

    public async Task RestoreNip05(string pubkey)
    {
        await using var db = _dbFactory.CreateContext();
        var identity = await db.Nip05Identities.FirstOrDefaultAsync(i => i.Pubkey == pubkey);
        if (identity != null) { identity.Revoked = false; }
        
        var wallet = await db.CustomerWallets.FirstOrDefaultAsync(w => w.Pubkey == pubkey);
        if (wallet != null) { wallet.Nip05Revoked = false; }
        
        await db.SaveChangesAsync();
        _logger.LogInformation("Restored NIP-05 for pubkey {Pubkey}", pubkey);
    }

    public async Task<List<Nip05UserDto>> ListAll()
    {
        await using var db = _dbFactory.CreateContext();
        var results = new List<Nip05UserDto>();

        var wallets = await db.CustomerWallets
            .Where(w => w.Nip05Username != null && w.Pubkey != null)
            .Select(w => new { w.Nip05Username, w.Pubkey, w.Nip05Revoked, w.CreatedAt })
            .ToListAsync();
        foreach (var w in wallets)
            results.Add(new Nip05UserDto
            {
                Username = w.Nip05Username!, Pubkey = w.Pubkey!,
                Revoked = w.Nip05Revoked, Source = "wallet", CreatedAt = w.CreatedAt
            });

        var identities = await db.Nip05Identities.ToListAsync();
        foreach (var i in identities)
            results.Add(new Nip05UserDto
            {
                Username = i.Username, Pubkey = i.Pubkey,
                Revoked = i.Revoked, Source = "standalone", CreatedAt = i.CreatedAt
            });

        return results.OrderBy(r => r.Username).ToList();
    }

    /// <summary>Release a NIP-05 identity (user self-revoke). Returns (success, error).</summary>
    public async Task<(bool Success, string? Error)> ReleaseNip05ForWallet(Guid walletId)
    {
        await using var db = _dbFactory.CreateContext();
        var wallet = await db.CustomerWallets.FirstOrDefaultAsync(w => w.Id == walletId);
        if (wallet == null) return (false, "Wallet not found");
        if (string.IsNullOrEmpty(wallet.Nip05Username) || string.IsNullOrEmpty(wallet.Pubkey))
            return (false, "Wallet has no NIP-05 identity");
        if (wallet.Nip05Revoked)
            return (false, "NIP-05 identity already revoked");

        // Check rate limit: 3 releases per day per wallet
        var todayUtc = DateTime.UtcNow.Date;
        var releasesToday = await db.WalletTransactions
            .CountAsync(t => t.CustomerWalletId == walletId
                && t.Type == WalletTransactionType.Nip05Released
                && t.CreatedAt >= todayUtc);
        if (releasesToday >= 3)
            return (false, "Rate limit: maximum 3 releases per day");

        var username = wallet.Nip05Username;
        var pubkey = wallet.Pubkey;

        // Revoke on wallet
        wallet.Nip05Revoked = true;
        wallet.Nip05Username = null;

        // Create/update standalone identity to track cooldown
        var identity = await db.Nip05Identities.FirstOrDefaultAsync(i => i.Pubkey == pubkey);
        if (identity != null)
        {
            identity.Revoked = true;
            identity.ReleasedAt = DateTime.UtcNow;
        }
        else
        {
            db.Nip05Identities.Add(new Nip05Identity
            {
                Pubkey = pubkey,
                Username = username,
                Revoked = true,
                ReleasedAt = DateTime.UtcNow,
                CreatedAt = wallet.CreatedAt
            });
        }

        // Log transaction
        db.WalletTransactions.Add(new WalletTransaction
        {
            CustomerWalletId = walletId,
            Type = WalletTransactionType.Nip05Released,
            Reference = $"Released NIP-05: {username}"
        });

        await db.SaveChangesAsync();
        _logger.LogInformation("Wallet {WalletId} released NIP-05 username {Username}", walletId, username);
        return (true, null);
    }

    /// <summary>
    /// Recover wallet by pubkey: return existing wallet, generate fresh token, restore NIP-05 if revoked.
    /// Returns (wallet, token, error).
    /// </summary>
    public async Task<(CustomerWallet? wallet, string? error)> RecoverWalletByPubkey(string pubkey)
    {
        await using var db = _dbFactory.CreateContext();
        var wallet = await db.CustomerWallets.FirstOrDefaultAsync(w => w.Pubkey == pubkey);
        if (wallet == null) return (null, null); // not found = new wallet flow

        // Restore NIP-05 if it was self-revoked (has a standalone identity with ReleasedAt)
        if (wallet.Nip05Revoked)
        {
            var identity = await db.Nip05Identities.FirstOrDefaultAsync(i => i.Pubkey == pubkey && i.ReleasedAt != null);
            if (identity != null)
            {
                // Reclaim: restore username to wallet, clear identity
                wallet.Nip05Username = identity.Username;
                wallet.Nip05Revoked = false;
                identity.Revoked = false;
                identity.ReleasedAt = null;
                _logger.LogInformation("Restored NIP-05 username {Username} for recovered wallet {WalletId}",
                    identity.Username, wallet.Id);
            }
        }

        // Log recovery
        db.WalletTransactions.Add(new WalletTransaction
        {
            CustomerWalletId = wallet.Id,
            Type = WalletTransactionType.WalletRecovery,
            Reference = "Wallet recovered via Schnorr signature"
        });

        await db.SaveChangesAsync();
        return (wallet, null);
    }

    /// <summary>Set pubkey and username on a wallet.</summary>
    public async Task SetWalletNip05(Guid walletId, string pubkey, string username)
    {
        await using var db = _dbFactory.CreateContext();
        var wallet = await db.CustomerWallets.FirstOrDefaultAsync(w => w.Id == walletId);
        if (wallet != null)
        {
            wallet.Pubkey = pubkey;
            wallet.Nip05Username = username;
            wallet.Nip05Revoked = false;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Update a wallet's NIP-05 username.</summary>
    public async Task UpdateWalletUsername(Guid walletId, string username)
    {
        await using var db = _dbFactory.CreateContext();
        var wallet = await db.CustomerWallets.FirstOrDefaultAsync(w => w.Id == walletId);
        if (wallet != null)
        {
            wallet.Nip05Username = username;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Check if a pubkey is already registered (in wallets or standalone identities).</summary>
    public async Task<bool> IsPubkeyRegistered(string pubkey)
    {
        await using var db = _dbFactory.CreateContext();
        return await db.CustomerWallets.AnyAsync(w => w.Pubkey == pubkey)
            || await db.Nip05Identities.AnyAsync(i => i.Pubkey == pubkey);
    }
}
