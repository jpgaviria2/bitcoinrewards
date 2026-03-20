using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Service to track idempotency keys and prevent duplicate operations (especially payments).
/// Uses in-memory cache with automatic cleanup of old entries.
/// </summary>
public class IdempotencyService
{
    private readonly ILogger<IdempotencyService> _logger;
    
    // Track completed operations: key = idempotency key, value = (result, timestamp)
    private static readonly ConcurrentDictionary<string, (object Result, DateTime Timestamp)> _completedOperations = new();
    
    // Default retention: 24 hours (must be long enough for client retries)
    private static readonly TimeSpan _retentionPeriod = TimeSpan.FromHours(24);

    public IdempotencyService(ILogger<IdempotencyService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Check if an operation with this idempotency key has already been processed.
    /// Returns the cached result if found, null if this is a new operation.
    /// </summary>
    public TResult? GetCachedResult<TResult>(string idempotencyKey) where TResult : class
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return null;

        if (_completedOperations.TryGetValue(idempotencyKey, out var cached))
        {
            // Check if still within retention period
            if (DateTime.UtcNow - cached.Timestamp < _retentionPeriod)
            {
                _logger.LogInformation("Idempotency key {Key} found in cache, returning cached result", idempotencyKey);
                return cached.Result as TResult;
            }
            else
            {
                // Expired, remove it
                _completedOperations.TryRemove(idempotencyKey, out _);
            }
        }

        return null;
    }

    /// <summary>
    /// Store the result of an operation with its idempotency key.
    /// </summary>
    public void CacheResult(string idempotencyKey, object result)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return;

        _completedOperations[idempotencyKey] = (result, DateTime.UtcNow);
        _logger.LogInformation("Cached result for idempotency key {Key}", idempotencyKey);
    }

    /// <summary>
    /// Generate a deterministic idempotency key from operation parameters.
    /// Use this when client doesn't provide their own key.
    /// </summary>
    public string GenerateKey(Guid walletId, string operation, params object[] parameters)
    {
        var payload = JsonSerializer.Serialize(new { walletId, operation, parameters });
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Background cleanup task to remove expired entries.
    /// Should be called periodically (e.g., every hour).
    /// </summary>
    public static int CleanupExpiredEntries()
    {
        var cutoff = DateTime.UtcNow - _retentionPeriod;
        var removed = 0;
        
        var keysToRemove = new System.Collections.Generic.List<string>();
        foreach (var kvp in _completedOperations)
        {
            if (kvp.Value.Timestamp < cutoff)
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            if (_completedOperations.TryRemove(key, out _))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// Get statistics about the idempotency cache.
    /// </summary>
    public (int TotalEntries, int ExpiredEntries) GetStatistics()
    {
        var cutoff = DateTime.UtcNow - _retentionPeriod;
        var total = _completedOperations.Count;
        var expired = 0;

        foreach (var kvp in _completedOperations)
        {
            if (kvp.Value.Timestamp < cutoff)
            {
                expired++;
            }
        }

        return (total, expired);
    }
}

/// <summary>
/// Request models that support idempotency keys.
/// </summary>
public interface IIdempotentRequest
{
    /// <summary>
    /// Client-provided idempotency key. If null, server generates one from request parameters.
    /// Must be unique per operation and stable across retries.
    /// Format: UUID or client-generated hash (max 64 chars).
    /// </summary>
    string? IdempotencyKey { get; set; }
}
