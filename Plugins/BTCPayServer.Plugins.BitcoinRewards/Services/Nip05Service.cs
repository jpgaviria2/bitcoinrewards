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
        
        var inIdentities = await db.Nip05Identities.AnyAsync(i => i.Username == lower);
        return !inIdentities;
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
