# Performance Optimization Guide

## Overview
Phase 2.7 adds caching, query optimization, and performance monitoring to the Bitcoin Rewards plugin.

## CachingService Integration

### Usage Example

```csharp
public class BitcoinRewardsService
{
    private readonly CachingService _cache;
    private readonly StoreRepository _storeRepository;
    
    public BitcoinRewardsService(CachingService cache, StoreRepository storeRepository)
    {
        _cache = cache;
        _storeRepository = storeRepository;
    }
    
    public async Task<BitcoinRewardsStoreSettings?> GetSettingsAsync(string storeId)
    {
        // Cache store settings (10 minute sliding expiration)
        return await _cache.GetOrCreateStoreSettingsAsync(
            storeId,
            BitcoinRewardsStoreSettings.SettingsName,
            async () => await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(
                storeId, 
                BitcoinRewardsStoreSettings.SettingsName));
    }
}
```

### Cache Invalidation

When settings are updated, invalidate the cache:

```csharp
// After saving settings
await _storeRepository.UpdateSetting(storeId, "BitcoinRewardsPluginSettings", settings);
_cache.InvalidateStoreSettings(storeId, "BitcoinRewardsPluginSettings");
```

## Query Optimization

### Index Recommendations

Add these indexes to improve query performance:

```sql
-- BitcoinRewards table
CREATE INDEX IX_BitcoinRewards_StoreId_Status_CreatedAt 
    ON "BitcoinRewards" ("StoreId", "Status", "CreatedAt" DESC);

CREATE INDEX IX_BitcoinRewards_StoreId_PullPaymentId 
    ON "BitcoinRewards" ("StoreId", "PullPaymentId") 
    WHERE "PullPaymentId" IS NOT NULL;

-- RewardErrors table (already has indexes from migration)
-- IX_RewardErrors_Timestamp
-- IX_RewardErrors_IsResolved_Timestamp
-- IX_RewardErrors_RewardId
-- IX_RewardErrors_IsRetryable_IsResolved
```

### Eager Loading

Use `.Include()` to avoid N+1 queries:

```csharp
// BAD: N+1 queries
var rewards = await context.BitcoinRewardRecords.ToListAsync();
foreach (var reward in rewards)
{
    var store = await context.Stores.FindAsync(reward.StoreId); // N queries
}

// GOOD: Single query with JOIN
var rewards = await context.BitcoinRewardRecords
    .Include(r => r.Store) // If navigation property exists
    .ToListAsync();
```

### Batch Operations

Process rewards in batches to reduce database round-trips:

```csharp
// Process rewards in batches of 100
var batch = new List<BitcoinRewardRecord>();
foreach (var reward in rewards)
{
    batch.Add(reward);
    
    if (batch.Count >= 100)
    {
        await context.BitcoinRewardRecords.AddRangeAsync(batch);
        await context.SaveChangesAsync();
        batch.Clear();
    }
}
// Don't forget remaining items
if (batch.Any())
{
    await context.BitcoinRewardRecords.AddRangeAsync(batch);
    await context.SaveChangesAsync();
}
```

## Connection Pooling

PostgreSQL connection pooling is configured in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=btcpayservermainnet;Username=postgres;Password=xxx;MinPoolSize=5;MaxPoolSize=100;ConnectionIdleLifetime=300"
  }
}
```

**Settings:**
- `MinPoolSize=5` - Keep 5 connections warm
- `MaxPoolSize=100` - Allow up to 100 concurrent connections
- `ConnectionIdleLifetime=300` - Close idle connections after 5 minutes

## Load Testing

### Apache Bench (Simple HTTP Load)

```bash
# Test webhook endpoint (100 requests, 10 concurrent)
ab -n 100 -c 10 -p webhook-payload.json -T application/json \
   https://anmore.cash/plugins/bitcoin-rewards/{storeId}/webhooks/square

# Test metrics endpoint
ab -n 1000 -c 50 \
   https://anmore.cash/api/v1/bitcoin-rewards/metrics
```

### k6 (Advanced Load Testing)

```javascript
// load-test.js
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '30s', target: 20 },  // Ramp up to 20 users
    { duration: '1m', target: 50 },   // Stay at 50 users
    { duration: '30s', target: 0 },   // Ramp down
  ],
  thresholds: {
    http_req_duration: ['p(95)<500'], // 95% of requests under 500ms
  },
};

export default function () {
  const res = http.get('https://anmore.cash/api/v1/bitcoin-rewards/metrics');
  check(res, {
    'status is 200': (r) => r.status === 200,
    'response time < 500ms': (r) => r.timings.duration < 500,
  });
  sleep(1);
}
```

Run: `k6 run load-test.js`

## Performance Targets

### Response Time Targets (p95)
- **Webhook processing:** <500ms
- **Reward creation:** <1s
- **Admin dashboard:** <2s
- **Metrics endpoint:** <100ms

### Throughput Targets
- **Webhooks:** 100 requests/second
- **Reward creation:** 50 rewards/second
- **API endpoints:** 200 requests/second

## Monitoring

### Prometheus Queries

```promql
# Average webhook processing time
rate(webhook_duration_ms_sum[5m]) / rate(webhook_duration_ms_count[5m])

# 95th percentile reward creation time
histogram_quantile(0.95, rate(claim_duration_seconds_bucket[5m]))

# Error rate
rate(errors_total[5m])

# Cache hit ratio (if implemented with custom metrics)
rate(cache_hits_total[5m]) / (rate(cache_hits_total[5m]) + rate(cache_misses_total[5m]))
```

### Grafana Dashboard

Import the Bitcoin Rewards dashboard template (future):

**Panels:**
1. Request rate by endpoint
2. Response time (p50/p95/p99)
3. Error rate by type
4. Active rewards gauge
5. Webhook processing latency
6. Database query duration

## Optimization Checklist

- [ ] Enable caching for store settings
- [ ] Enable caching for payout processors
- [ ] Add database indexes
- [ ] Configure connection pooling
- [ ] Run load tests
- [ ] Monitor metrics in Grafana
- [ ] Set up alerting for slow queries (>1s)
- [ ] Implement circuit breaker for external APIs (future)
- [ ] Consider Redis for distributed caching (multi-instance deployments)

## Redis (Optional - Future)

For multi-instance BTCPay deployments, use Redis for distributed caching:

```csharp
// In BitcoinRewardsPlugin.cs
services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = Configuration.GetConnectionString("Redis");
    options.InstanceName = "BitcoinRewards:";
});

// Replace IMemoryCache with IDistributedCache
services.AddSingleton<ICacheService, RedisCacheService>();
```

## Database Maintenance

### Vacuum (PostgreSQL)

Run periodic vacuum to reclaim space and update statistics:

```sql
VACUUM ANALYZE "BitcoinRewards";
VACUUM ANALYZE "RewardErrors";
```

### Archive Old Records

Archive rewards older than 90 days to keep tables small:

```sql
-- Move to archive table
INSERT INTO "BitcoinRewards_Archive" 
SELECT * FROM "BitcoinRewards" 
WHERE "CreatedAt" < NOW() - INTERVAL '90 days';

-- Delete from main table
DELETE FROM "BitcoinRewards" 
WHERE "CreatedAt" < NOW() - INTERVAL '90 days';
```

---

**Last Updated:** 2026-03-28  
**Phase:** 2.7 - Performance Optimization  
**Status:** Complete
