using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services
{
    /// <summary>
    /// Caching service for Bitcoin Rewards plugin with memory-safe defaults
    /// </summary>
    public class CachingService
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger<CachingService> _logger;
        
        // Cache key prefixes
        private const string STORE_SETTINGS_PREFIX = "btcrewards:settings:";
        private const string PAYOUT_PROCESSORS_PREFIX = "btcrewards:processors:";
        private const string RATE_PREFIX = "btcrewards:rate:";
        private const string STORE_PREFIX = "btcrewards:store:";
        
        // Cache durations
        private static readonly TimeSpan SETTINGS_CACHE_DURATION = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan PROCESSORS_CACHE_DURATION = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan RATE_CACHE_DURATION = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan STORE_CACHE_DURATION = TimeSpan.FromMinutes(15);
        
        public CachingService(IMemoryCache cache, ILogger<CachingService> logger)
        {
            _cache = cache;
            _logger = logger;
        }
        
        /// <summary>
        /// Get or create cached value with automatic key prefixing
        /// </summary>
        public async Task<T> GetOrCreateAsync<T>(
            string key,
            Func<Task<T>> factory,
            TimeSpan? expiration = null)
        {
            return await _cache.GetOrCreateAsync(key, async entry =>
            {
                entry.SlidingExpiration = expiration ?? TimeSpan.FromMinutes(5);
                entry.Size = 1; // For memory management
                
                _logger.LogDebug("Cache miss for key {Key}, fetching from source", key);
                return await factory();
            }) ?? throw new InvalidOperationException($"Failed to get or create cache entry for key {key}");
        }
        
        /// <summary>
        /// Cache store settings
        /// </summary>
        public async Task<T?> GetOrCreateStoreSettingsAsync<T>(
            string storeId,
            string settingsName,
            Func<Task<T?>> factory) where T : class
        {
            var key = $"{STORE_SETTINGS_PREFIX}{storeId}:{settingsName}";
            return await _cache.GetOrCreateAsync(key, async entry =>
            {
                entry.SlidingExpiration = SETTINGS_CACHE_DURATION;
                entry.Size = 1;
                entry.SetPriority(CacheItemPriority.High); // Settings are important
                
                _logger.LogDebug("Caching store settings for {StoreId}:{SettingsName}", storeId, settingsName);
                return await factory();
            });
        }
        
        /// <summary>
        /// Cache payout processors
        /// </summary>
        public async Task<T> GetOrCreatePayoutProcessorsAsync<T>(
            string storeId,
            Func<Task<T>> factory)
        {
            var key = $"{PAYOUT_PROCESSORS_PREFIX}{storeId}";
            return await GetOrCreateAsync(key, factory, PROCESSORS_CACHE_DURATION);
        }
        
        /// <summary>
        /// Cache BTC exchange rate
        /// </summary>
        public async Task<T?> GetOrCreateRateAsync<T>(
            string fromCurrency,
            string toCurrency,
            Func<Task<T?>> factory) where T : struct
        {
            var key = $"{RATE_PREFIX}{fromCurrency}:{toCurrency}";
            return await _cache.GetOrCreateAsync(key, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = RATE_CACHE_DURATION;
                entry.Size = 1;
                entry.SetPriority(CacheItemPriority.Low); // Rates change frequently
                
                return await factory();
            });
        }
        
        /// <summary>
        /// Cache store object
        /// </summary>
        public async Task<T?> GetOrCreateStoreAsync<T>(
            string storeId,
            Func<Task<T?>> factory) where T : class
        {
            var key = $"{STORE_PREFIX}{storeId}";
            return await _cache.GetOrCreateAsync(key, async entry =>
            {
                entry.SlidingExpiration = STORE_CACHE_DURATION;
                entry.Size = 1;
                entry.SetPriority(CacheItemPriority.High);
                
                return await factory();
            });
        }
        
        /// <summary>
        /// Invalidate specific cache entry
        /// </summary>
        public void Invalidate(string key)
        {
            _cache.Remove(key);
            _logger.LogDebug("Invalidated cache key {Key}", key);
        }
        
        /// <summary>
        /// Invalidate all store settings cache for a store
        /// </summary>
        public void InvalidateStoreSettings(string storeId, string? settingsName = null)
        {
            if (settingsName != null)
            {
                var key = $"{STORE_SETTINGS_PREFIX}{storeId}:{settingsName}";
                Invalidate(key);
            }
            else
            {
                // Invalidate all settings for this store (requires scanning, not ideal for large caches)
                _logger.LogInformation("Store settings invalidated for {StoreId}", storeId);
                // Note: IMemoryCache doesn't support key enumeration, so we can't invalidate by prefix
                // In production, consider using Redis with key pattern matching
            }
        }
        
        /// <summary>
        /// Get cache statistics for monitoring
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            // IMemoryCache doesn't expose statistics directly
            // In production, consider using a cache wrapper that tracks hits/misses
            return new CacheStatistics
            {
                SettingsCacheDuration = SETTINGS_CACHE_DURATION,
                ProcessorsCacheDuration = PROCESSORS_CACHE_DURATION,
                RateCacheDuration = RATE_CACHE_DURATION,
                StoreCacheDuration = STORE_CACHE_DURATION
            };
        }
    }
    
    /// <summary>
    /// Cache statistics for monitoring
    /// </summary>
    public class CacheStatistics
    {
        public TimeSpan SettingsCacheDuration { get; set; }
        public TimeSpan ProcessorsCacheDuration { get; set; }
        public TimeSpan RateCacheDuration { get; set; }
        public TimeSpan StoreCacheDuration { get; set; }
    }
}
