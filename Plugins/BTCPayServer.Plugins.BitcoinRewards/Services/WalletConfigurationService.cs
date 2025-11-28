#nullable enable
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Service for managing wallet configuration (mint URLs) per store.
/// Stores configuration in our own database, with optional fallback to Cashu plugin.
/// </summary>
public class WalletConfigurationService
{
    private readonly BitcoinRewardsPluginDbContextFactory _dbContextFactory;
    private readonly ILogger<WalletConfigurationService> _logger;
    private readonly object? _paymentMethodHandlers; // For optional Cashu plugin fallback
    private readonly Assembly? _cashuAssembly; // For optional Cashu plugin fallback

    public WalletConfigurationService(
        BitcoinRewardsPluginDbContextFactory dbContextFactory,
        ILogger<WalletConfigurationService> logger,
        IServiceProvider serviceProvider)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;

        // Try to discover Cashu plugin for optional fallback
        try
        {
            _cashuAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "BTCPayServer.Plugins.Cashu" ||
                                    a.FullName?.Contains("Cashu") == true);

            if (_cashuAssembly != null)
            {
                // Try to get payment method handlers for fallback
                var handlersType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "PaymentMethodHandlerDictionary");
                if (handlersType != null)
                {
                    _paymentMethodHandlers = serviceProvider.GetService(handlersType);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not discover Cashu plugin for fallback");
        }
    }

    /// <summary>
    /// Get mint URL for a store (checks our database first, then Cashu plugin as fallback)
    /// </summary>
    public async Task<string?> GetMintUrlAsync(string storeId)
    {
        try
        {
            // Method 1: Check our own database
            await using var db = _dbContextFactory.CreateContext();
            var mint = await db.Mints
                .Where(m => m.StoreId == storeId && m.IsActive)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync();

            if (mint != null && !string.IsNullOrEmpty(mint.Url))
            {
                _logger.LogDebug("Found mint URL from Bitcoin Rewards database for store {StoreId}: {MintUrl}", 
                    storeId, mint.Url);
                return mint.Url;
            }

            // Method 2: Fallback to Cashu plugin payment method config
            if (_cashuAssembly != null && _paymentMethodHandlers != null)
            {
                try
                {
                    var cashuPluginType = _cashuAssembly.GetType("BTCPayServer.Plugins.Cashu.CashuPlugin");
                    if (cashuPluginType != null)
                    {
                        var cashuPmidField = cashuPluginType.GetField("CashuPmid",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                        if (cashuPmidField != null)
                        {
                            var cashuPmid = cashuPmidField.GetValue(null);
                            if (cashuPmid != null)
                            {
                                // This would require StoreData, which we don't have here
                                // We'll handle this fallback in CashuServiceAdapter instead
                                _logger.LogDebug("Cashu plugin available for fallback, but mint URL should be retrieved via CashuServiceAdapter");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not get mint URL from Cashu plugin (fallback failed)");
                }
            }

            _logger.LogWarning("No mint URL configured for store {StoreId} in Bitcoin Rewards database", storeId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting mint URL for store {StoreId}", storeId);
            return null;
        }
    }

    /// <summary>
    /// Result of setting mint URL
    /// </summary>
    public class SetMintUrlResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Set mint URL for a store
    /// </summary>
    public async Task<SetMintUrlResult> SetMintUrlAsync(string storeId, string mintUrl, string unit = "sat", bool enabled = true)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(mintUrl))
            {
                _logger.LogWarning("Cannot set empty mint URL for store {StoreId}", storeId);
                return new SetMintUrlResult
                {
                    Success = false,
                    ErrorMessage = "Mint URL cannot be empty"
                };
            }

            await using var db = _dbContextFactory.CreateContext();

            // Deactivate existing mints for this store
            var existingMints = await db.Mints
                .Where(m => m.StoreId == storeId && m.IsActive)
                .ToListAsync();

            foreach (var existingMint in existingMints)
            {
                existingMint.IsActive = false;
                existingMint.UpdatedAt = DateTime.UtcNow;
            }

            // Check if this mint URL already exists (inactive)
            var existingMintRecord = await db.Mints
                .FirstOrDefaultAsync(m => m.StoreId == storeId && m.Url == mintUrl);

            if (existingMintRecord != null)
            {
                // Reactivate existing mint
                existingMintRecord.IsActive = true;
                existingMintRecord.Unit = unit;
                existingMintRecord.Enabled = enabled;
                existingMintRecord.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                // Create new mint
                var newMint = new Mint
                {
                    Id = Guid.NewGuid(),
                    StoreId = storeId,
                    Url = mintUrl.TrimEnd('/'), // Remove trailing slash for consistency
                    Unit = unit,
                    IsActive = true,
                    Enabled = enabled,
                    CreatedAt = DateTime.UtcNow
                };
                db.Mints.Add(newMint);
            }

            await db.SaveChangesAsync();
            _logger.LogInformation("Set mint URL for store {StoreId}: {MintUrl}", storeId, mintUrl);
            return new SetMintUrlResult { Success = true };
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error setting mint URL for store {StoreId}", storeId);
            var errorMessage = "Database error occurred while saving mint URL configuration.";
            if (ex.InnerException != null)
            {
                errorMessage += $" {ex.InnerException.Message}";
            }
            return new SetMintUrlResult
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting mint URL for store {StoreId}", storeId);
            return new SetMintUrlResult
            {
                Success = false,
                ErrorMessage = $"Failed to save mint URL configuration: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Get wallet configuration for a store
    /// </summary>
    public async Task<WalletConfiguration?> GetConfigurationAsync(string storeId)
    {
        try
        {
            await using var db = _dbContextFactory.CreateContext();
            var mint = await db.Mints
                .Where(m => m.StoreId == storeId && m.IsActive)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync();

            if (mint == null)
            {
                return null;
            }

            // Get balance - exclude proofs in FailedTransactions (matching Cashu plugin)
            var balanceDecimal = await db.Proofs
                .Where(p => p.StoreId == storeId && p.MintUrl == mint.Url
                    && !db.FailedTransactions.Any(ft => ft.UsedProofs.Contains(p)))
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            return new WalletConfiguration
            {
                StoreId = storeId,
                MintUrl = mint.Url,
                Unit = mint.Unit,
                Balance = (ulong)balanceDecimal,
                Enabled = mint.Enabled,
                CreatedAt = mint.CreatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting configuration for store {StoreId}", storeId);
            return null;
        }
    }
}

/// <summary>
/// Wallet configuration model
/// </summary>
public class WalletConfiguration
{
    public string StoreId { get; set; } = string.Empty;
    public string MintUrl { get; set; } = string.Empty;
    public string Unit { get; set; } = "sat";
    public ulong Balance { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

