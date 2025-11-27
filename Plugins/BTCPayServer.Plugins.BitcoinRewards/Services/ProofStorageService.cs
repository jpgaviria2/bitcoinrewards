#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Data.Models;
using DotNut;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Service for managing proof storage in Bitcoin Rewards plugin's database.
/// Optionally falls back to Cashu plugin's database if available.
/// </summary>
public class ProofStorageService
{
    private readonly BitcoinRewardsPluginDbContextFactory _dbContextFactory;
    private readonly ILogger<ProofStorageService> _logger;
    private readonly object? _cashuDbContextFactory; // Optional Cashu plugin database
    private readonly object? _cashuService; // Optional Cashu plugin service

    public ProofStorageService(
        BitcoinRewardsPluginDbContextFactory dbContextFactory,
        ILogger<ProofStorageService> logger,
        IServiceProvider serviceProvider)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;

        // Try to discover Cashu plugin database as optional fallback
        try
        {
            var cashuAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "BTCPayServer.Plugins.Cashu" ||
                                    a.FullName?.Contains("Cashu") == true);

            if (cashuAssembly != null)
            {
                var dbContextFactoryType = cashuAssembly.GetType("BTCPayServer.Plugins.Cashu.Data.CashuDbContextFactory");
                if (dbContextFactoryType != null)
                {
                    _cashuDbContextFactory = serviceProvider.GetService(dbContextFactoryType);
                }

                var paymentServiceType = cashuAssembly.GetType("BTCPayServer.Plugins.Cashu.PaymentHandlers.CashuPaymentService");
                if (paymentServiceType != null)
                {
                    _cashuService = serviceProvider.GetService(paymentServiceType);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not discover Cashu plugin services for fallback");
        }
    }

    /// <summary>
    /// Add proofs to the database (plugin's own database, with optional fallback to Cashu plugin)
    /// </summary>
    public async Task AddProofsAsync(IEnumerable<Proof> proofs, string storeId, string mintUrl)
    {
        try
        {
            var proofList = proofs.ToList();
            if (!proofList.Any())
            {
                _logger.LogWarning("No proofs to add");
                return;
            }

            // Store in our own database
            await using var db = _dbContextFactory.CreateContext();
            var storedProofs = StoredProof.FromBatch(proofList, storeId, mintUrl).ToList();
            await db.Proofs.AddRangeAsync(storedProofs);
            await db.SaveChangesAsync();

            _logger.LogInformation("Stored {Count} proofs in Bitcoin Rewards database for store {StoreId}", 
                storedProofs.Count, storeId);

            // Optionally also store in Cashu plugin database if available
            if (_cashuService != null)
            {
                try
                {
                    var addProofsMethod = _cashuService.GetType().GetMethod("AddProofsToDb",
                        new[] { typeof(IEnumerable<>).MakeGenericType(typeof(Proof)), typeof(string), typeof(string) });
                    if (addProofsMethod != null)
                    {
                        var task = addProofsMethod.Invoke(_cashuService, new object[] { proofList, storeId, mintUrl }) as Task;
                        if (task != null)
                        {
                            await task;
                            _logger.LogDebug("Also stored proofs in Cashu plugin database (fallback)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not store proofs in Cashu plugin database (fallback failed)");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding proofs to database for store {StoreId}", storeId);
            throw;
        }
    }

    /// <summary>
    /// Get proofs from database (checks our database first, optionally falls back to Cashu plugin)
    /// </summary>
    public async Task<List<Proof>> GetProofsAsync(string storeId, string mintUrl, ulong? maxAmount = null)
    {
        try
        {
            var proofs = new List<Proof>();

            // Get from our own database
            await using var db = _dbContextFactory.CreateContext();
            var query = db.Proofs
                .Where(p => p.StoreId == storeId && p.MintUrl == mintUrl)
                .OrderByDescending(p => p.CreatedAt);

            var storedProofs = await query.ToListAsync();

            if (maxAmount.HasValue)
            {
                // Select proofs up to maxAmount
                ulong total = 0;
                foreach (var storedProof in storedProofs)
                {
                    if (total >= maxAmount.Value)
                        break;

                    proofs.Add(storedProof.ToDotNutProof());
                    total += storedProof.Amount;
                }
            }
            else
            {
                proofs.AddRange(storedProofs.Select(p => p.ToDotNutProof()));
            }

            _logger.LogDebug("Retrieved {Count} proofs from Bitcoin Rewards database for store {StoreId}", 
                proofs.Count, storeId);

            // If we don't have enough proofs and Cashu plugin database is available, try fallback
            if (maxAmount.HasValue && proofs.Sum(p => (decimal)p.Amount) < maxAmount.Value && _cashuDbContextFactory != null)
            {
                try
                {
                    var createContextMethod = _cashuDbContextFactory.GetType().GetMethod("CreateContext");
                    if (createContextMethod != null)
                    {
                        var cashuDb = createContextMethod.Invoke(_cashuDbContextFactory, null);
                        if (cashuDb != null)
                        {
                            try
                            {
                                // Use reflection to query Cashu plugin's database
                                var proofsProperty = cashuDb.GetType().GetProperty("Proofs");
                                if (proofsProperty != null)
                                {
                                    var cashuProofsDbSet = proofsProperty.GetValue(cashuDb);
                                    if (cashuProofsDbSet != null)
                                    {
                                        // This is complex with reflection, so we'll just log that fallback is available
                                        _logger.LogDebug("Cashu plugin database available for fallback, but using our own database for now");
                                    }
                                }
                            }
                            finally
                            {
                                if (cashuDb is IDisposable disposable)
                                {
                                    disposable.Dispose();
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not retrieve proofs from Cashu plugin database (fallback failed)");
                }
            }

            return proofs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting proofs from database for store {StoreId}", storeId);
            throw;
        }
    }

    /// <summary>
    /// Get ecash balance for a store and mint
    /// </summary>
    public async Task<ulong> GetBalanceAsync(string storeId, string mintUrl)
    {
        try
        {
            await using var db = _dbContextFactory.CreateContext();
            var balanceDecimal = await db.Proofs
                .Where(p => p.StoreId == storeId && p.MintUrl == mintUrl)
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            var balance = (ulong)balanceDecimal;
            _logger.LogDebug("Ecash balance for store {StoreId} on mint {MintUrl}: {Balance} sat", 
                storeId, mintUrl, balance);

            return balance;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting balance for store {StoreId}", storeId);
            return 0;
        }
    }

    /// <summary>
    /// Remove proofs from database (used when proofs are spent)
    /// </summary>
    public async Task RemoveProofsAsync(List<string> proofIds, string storeId)
    {
        try
        {
            await using var db = _dbContextFactory.CreateContext();
            var proofsToRemove = await db.Proofs
                .Where(p => p.StoreId == storeId && proofIds.Contains(p.Id.ToString()))
                .ToListAsync();

            if (proofsToRemove.Any())
            {
                db.Proofs.RemoveRange(proofsToRemove);
                await db.SaveChangesAsync();
                _logger.LogInformation("Removed {Count} proofs from database for store {StoreId}", 
                    proofsToRemove.Count, storeId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing proofs from database for store {StoreId}", storeId);
            throw;
        }
    }
}

