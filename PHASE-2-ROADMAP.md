# Phase 2: Production Hardening - Development Roadmap

## Overview
Phase 2 transforms the Bitcoin Rewards plugin from a functional MVP into a production-grade system with comprehensive error handling, metrics, monitoring, and performance optimization.

**Status:** 45% Complete (3/7 major features)  
**Timeline:** 28 days total (currently Day 5)  
**Branch:** `feature/phase-3-testing`

---

## ✅ Completed Features (Days 1-8)

### 2.1 Health Checks System (Days 1-2) ✅
**Files:**
- `HealthChecks/BitcoinRewardsHealthCheck.cs` (4.5 KB)
- Registration in `BitcoinRewardsPlugin.cs`

**Capabilities:**
- Database connectivity monitoring
- Reward success rate tracking (90% threshold)
- Stuck rewards detection (24-hour window)
- Integrates with BTCPay Server's `/health` endpoint
- Prometheus-compatible health status

**Commit:** `d02e7f8`

---

### 2.2 Enhanced Error Handling (Days 3-6) ✅
**Files:**
- `Exceptions/BitcoinRewardsException.cs` (5.2 KB)
- `Models/RewardError.cs` (2.1 KB)
- `Services/ErrorTrackingService.cs` (7.8 KB)
- `Data/Migrations/20260328000000_AddErrorTracking.cs` (3.5 KB)
- `ViewModels/ErrorDashboardViewModel.cs` (0.8 KB)
- `Views/UIBitcoinRewards/ErrorDashboard.cshtml` (13 KB)
- Updates to `BitcoinRewardsDbContext.cs`
- Updates to `UIBitcoinRewardsController.cs`

**Capabilities:**

#### Exception Hierarchy (30+ types)
```csharp
BitcoinRewardsException (base)
├── WebhookException
│   ├── WebhookSignatureInvalidException
│   ├── WebhookPayloadInvalidException
│   └── WebhookTimeoutException
├── LightningException
│   ├── PullPaymentCreationFailedException
│   ├── InvoiceGenerationFailedException
│   └── LightningNodeUnavailableException
├── ApiException
│   ├── SquareApiException
│   ├── ShopifyApiException
│   └── RateLimitExceededException
├── DatabaseException
│   ├── DuplicateRewardException
│   └── RewardNotFoundExcept
├── BusinessLogicException
│   ├── InsufficientBalanceException
│   ├── RewardAmountTooSmallException
│   └── RewardCapExceededException
└── ConfigurationException
    ├── MissingApiKeyException
    └── InvalidSettingsException
```

**Features:**
- Retryability detection (automatic recovery eligibility)
- User-friendly error messages (no technical leaks)
- Contextual information (OrderId, StoreId, RewardId)

#### Error Tracking Database
**Table:** `RewardErrors`
- Full error details with stack traces
- Resolution tracking (admin can mark resolved)
- Retry counting and timestamps
- Comprehensive indexes for fast queries

**Indexes:**
- `IX_RewardErrors_Timestamp` - Time-based queries
- `IX_RewardErrors_IsResolved_Timestamp` - Admin dashboard filtering
- `IX_RewardErrors_RewardId` - Reward-specific history
- `IX_RewardErrors_IsRetryable_IsResolved` - Auto-recovery queries

#### ErrorTrackingService API
```csharp
Task LogErrorAsync(string storeId, string? rewardId, string operation, 
                   string errorMessage, string? stackTrace, bool isRetryable, 
                   Dictionary<string, string>? context)
Task<List<RewardError>> GetRecentErrorsAsync(string storeId, int days, bool? resolved)
Task<ErrorStatistics> GetErrorStatisticsAsync(string storeId, int days)
Task ResolveErrorAsync(int errorId, string resolvedBy)
Task RecordRetryAttemptAsync(int errorId)
Task<List<RewardError>> GetRetryableErrorsAsync(string storeId)
```

#### Admin Error Dashboard
**Route:** `/plugins/bitcoin-rewards/{storeId}/errors`

**Features:**
- **Statistics cards:** Total errors, unresolved count, retryable count, time range
- **Error breakdown:** By type and operation (top offenders)
- **Filtering:** 1-90 days, resolved/unresolved/all
- **Expandable details:** Full stack traces, context data, retry history
- **Resolution workflow:** One-click "Resolve" button per error
- **Color coding:** Red (failed), yellow (retryable), green (resolved)

**Commits:** `27a9a75`, `21d9605`, `a717ad4`

---

### 2.3 Metrics & Telemetry (Days 7-8) ✅
**Files:**
- `Services/RewardMetrics.cs` (12.3 KB)
- `Controllers/MetricsController.cs` (3.2 KB)
- Integration in `BitcoinRewardsService.cs`
- Integration in `SquareWebhookController.cs`

**Capabilities:**

#### RewardMetrics Service
**Memory-safe design:** Fixed 1000-observation ring buffers prevent unbounded growth

**Counters:**
```
rewards_created_total{platform="square",store="abc"}
rewards_claimed_total{platform="square",store="abc"}
errors_total{error_type="webhook",store="abc"}
webhooks_received_total{platform="square",store="abc"}
lightning_operations_total{operation="pull_payment_created",store="abc",success="true"}
```

**Gauges:**
```
active_rewards{store="abc"}
unclaimed_value_satoshis{store="abc"}
```

**Histograms (with p50/p95/p99):**
```
reward_amount_satoshis
claim_duration_seconds
webhook_duration_ms{platform="square",store="abc"}
```

#### Metrics API Endpoints
**Prometheus format** (public, for scrapers):
```
GET /api/v1/bitcoin-rewards/metrics
Content-Type: text/plain; version=0.0.4
```

**JSON format** (admin, auth required):
```
GET /api/v1/bitcoin-rewards/metrics/json
Content-Type: application/json
```

**Health check:**
```
GET /api/v1/bitcoin-rewards/metrics/health
Content-Type: application/json
```

#### Integration Points
**BitcoinRewardsService:**
- `RecordRewardCreated()` on successful pull payment
- `RecordRewardAmount()` for amount tracking
- `RecordError()` at all validation failure points
- `RecordLightningOperation()` for pull payment success/failure
- Full error context in catch blocks

**SquareWebhookController:**
- `RecordWebhookReceived()` on webhook arrival
- `RecordWebhookDuration()` with success/failure tracking
- `RecordError()` for webhook processing failures

**Commits:** `671de69`, `bcc664a`, `a717ad4`

---

## ⏳ Remaining Features (Days 9-28)

### 2.4 Rate Limiting Middleware (Days 9-13) 🔜
**Priority:** HIGH - Prevents abuse and DoS attacks

**Files to create:**
- `Middleware/RateLimitingMiddleware.cs`
- `Services/RateLimitService.cs`
- `Models/RateLimitPolicy.cs`
- `Views/UIBitcoinRewards/RateLimitSettings.cshtml`

**Implementation:**

#### Token Bucket Algorithm
```csharp
public class RateLimitPolicy
{
    public int RequestsPerMinute { get; set; } = 60;
    public int BurstSize { get; set; } = 10;
    public TimeSpan WindowDuration { get; set; } = TimeSpan.FromMinutes(1);
}
```

**Per-IP limits:**
- Webhook endpoints: 60 req/min
- API endpoints: 120 req/min
- Admin UI: 300 req/min

**Per-store limits:**
- Webhook processing: 100 req/min per store
- Reward creation: 50 req/min per store

#### Redis Backing Store (Distributed)
- Token state stored in Redis for multi-instance deployments
- Sliding window counters
- Automatic expiration

#### Response Headers
```
X-RateLimit-Limit: 60
X-RateLimit-Remaining: 42
X-RateLimit-Reset: 1234567890
Retry-After: 30
```

#### Admin Configuration
- Per-store rate limit overrides
- Whitelist/blacklist IP addresses
- Rate limit history and violation logs

**Testing:**
- Unit tests with mock Redis
- Integration tests with actual rate limit violations
- Load testing with Apache Bench

---

### 2.5 Auto-Recovery Watchdog (Days 14-18) 🔜
**Priority:** HIGH - Automatic retry of failed operations

**Files to create:**
- `Services/AutoRecoveryWatchdog.cs` (BackgroundService)
- `Services/RetryPolicyService.cs`
- `Models/RetryPolicy.cs`

**Implementation:**

#### Background Service
```csharp
public class AutoRecoveryWatchdog : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RecoverOrphanedRewards();
            await RetryFailedOperations();
            await EscalateMaxRetries();
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
```

#### Retry Strategy (Exponential Backoff)
```
Attempt 1: Immediate
Attempt 2: 5 minutes
Attempt 3: 15 minutes (5 * 3)
Attempt 4: 45 minutes (15 * 3)
Attempt 5: 2.25 hours (45 * 3)
Max: 5 attempts, then escalate to admin
```

**Recoverable operations:**
- Orphaned rewards (DB record exists, no pull payment)
- Failed webhook deliveries
- Failed email notifications
- Lightning node temporary failures
- Rate provider temporary failures

#### Escalation
- After max retries, log error to admin dashboard
- Optional: Send notification via BTCPay notification system
- Mark error as "needs manual intervention"

**Metrics:**
- `auto_recovery_attempts_total{operation,success}`
- `auto_recovery_duration_seconds`
- `escalations_total{operation}`

---

### 2.6 Advanced Logging (Days 19-23) 🔜
**Priority:** MEDIUM - Debugging and audit trail

**Files to create:**
- `Middleware/CorrelationIdMiddleware.cs`
- `Logging/SerilogEnrichers.cs`
- `Services/LogAggregationService.cs`
- `Views/UIBitcoinRewards/LogViewer.cshtml`

**Implementation:**

#### Correlation IDs
- Generate `X-Correlation-Id` for every request
- Thread-local storage for correlation ID
- Include in all log messages
- Return in response headers

**Example:**
```
[2026-03-28 20:00:00] [INFO] [CorrelationId: abc123] BitcoinRewardsService: Processing reward for transaction tx_456
[2026-03-28 20:00:01] [INFO] [CorrelationId: abc123] PullPaymentService: Creating pull payment for 5000 sats
[2026-03-28 20:00:02] [INFO] [CorrelationId: abc123] EmailService: Sending notification to user@example.com
```

#### Structured Logging (Serilog)
```csharp
Log.Information("Reward created {@Reward} for {StoreId}", 
    new { RewardId = reward.Id, Amount = reward.RewardAmountSatoshis },
    storeId);
```

**Enrichers:**
- User ID (for admin actions)
- Store ID (automatic context)
- Request path and method
- Client IP address
- User agent

#### Log Aggregation
- Optional ELK stack integration (Elasticsearch, Logstash, Kibana)
- Serilog sinks: File, Console, Seq, Elasticsearch
- Log retention policies (30 days default)

#### Admin Log Viewer
**Route:** `/plugins/bitcoin-rewards/{storeId}/logs`

**Features:**
- Filter by correlation ID, level, time range
- Full-text search
- Export logs to JSON/CSV
- Real-time log tailing (SignalR)

---

### 2.7 Performance Optimization (Days 24-28) 🔜
**Priority:** MEDIUM - Scalability and efficiency

**Files to update:**
- `Services/BitcoinRewardsService.cs` (add caching)
- `Data/BitcoinRewardsRepository.cs` (query optimization)
- `appsettings.json` (connection pooling)

**Implementation:**

#### Caching Layer (IMemoryCache)
**Cache store settings:**
```csharp
var settings = await _cache.GetOrCreateAsync(
    $"settings:{storeId}",
    async entry => {
        entry.SlidingExpiration = TimeSpan.FromMinutes(10);
        return await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(storeId);
    });
```

**Cache payout processors:**
- Avoid repeated queries for processor availability
- Refresh every 5 minutes

**Cache BTC rates:**
- Store fetched rates for 30 seconds
- Reduces external API calls during high-volume periods

#### Query Optimization
**Index analysis:**
```sql
EXPLAIN ANALYZE SELECT * FROM "BitcoinRewards" WHERE "StoreId" = 'abc' AND "Status" = 1;
```

**Add missing indexes:**
- `IX_BitcoinRewards_StoreId_Status`
- `IX_BitcoinRewards_CreatedAt`
- `IX_BitcoinRewards_TransactionId` (already has unique constraint)

**Batch queries:**
- Fetch recent rewards in single query instead of N+1
- Use `Include()` for eager loading related entities

#### Connection Pooling
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "...;MinPoolSize=5;MaxPoolSize=100;ConnectionIdleLifetime=300"
  }
}
```

#### Load Testing
**Tools:** Apache Bench, k6, Locust

**Test scenarios:**
- 100 concurrent webhook requests
- 1000 rewards created in 60 seconds
- Admin dashboard under load

**Targets:**
- Webhook processing: <500ms p95
- Reward creation: <1s p95
- Admin dashboard: <2s p95

#### Metrics
- `cache_hit_ratio{cache_type}`
- `query_duration_seconds{operation}`
- `connection_pool_size{state="active|idle"}`

---

## Testing Strategy

### Unit Tests (Phase 3)
**Not yet implemented** - Planned for Phase 3 (v1.4.0)

**Target coverage:** 80%+

**Key test suites:**
- `ErrorTrackingServiceTests.cs`
- `RewardMetricsTests.cs`
- `RateLimitingTests.cs`
- `AutoRecoveryWatchdogTests.cs`
- `BitcoinRewardsServiceTests.cs`

### Integration Tests (Phase 3)
- End-to-end reward creation
- Webhook signature validation
- Rate limit enforcement
- Auto-recovery workflows

### Load Tests (Phase 2.7)
- Concurrent webhook handling
- High-volume reward creation
- Metrics endpoint under load

---

## Deployment Plan

### Development Environment
```bash
cd /home/btcpay/bitcoin-rewards-dev
git checkout feature/phase-3-testing
dotnet build Plugins/BTCPayServer.Plugins.BitcoinRewards
# Copy DLL to test container
docker cp Plugins/.../bin/Debug/.../BTCPayServer.Plugins.BitcoinRewards.dll generated_btcpayserver_1:/datadir/Plugins/
docker restart generated_btcpayserver_1
```

### Production Deployment
**After Phase 2 complete:**
1. Merge feature branch to `main`
2. Tag release: `v1.3.0-rc1` (release candidate)
3. Test on staging environment (7 days)
4. Monitor metrics and error rates
5. Tag stable release: `v1.3.0`
6. Deploy to production via BTCPay plugin upload UI

### Rollback Plan
**If critical issues found:**
1. Keep backup of previous plugin DLL
2. Docker cp previous version back
3. Restart BTCPay container
4. Database migrations are backwards-compatible (no rollback needed)

---

## Monitoring and Observability

### Prometheus Metrics Scraping
```yaml
scrape_configs:
  - job_name: 'btcpay-bitcoin-rewards'
    static_configs:
      - targets: ['anmore.cash:443']
    metrics_path: /api/v1/bitcoin-rewards/metrics
    scheme: https
```

### Grafana Dashboard (Future)
**Panels:**
- Reward creation rate (per minute)
- Error rate by type
- Webhook processing latency (p50/p95/p99)
- Active rewards gauge
- Unclaimed value (satoshis)

### Alerting Rules
```yaml
- alert: HighErrorRate
  expr: rate(errors_total[5m]) > 0.1
  for: 5m
  annotations:
    summary: "High error rate detected in Bitcoin Rewards"

- alert: WebhookLatencyHigh
  expr: histogram_quantile(0.95, webhook_duration_ms) > 2000
  for: 10m
  annotations:
    summary: "Webhook p95 latency above 2 seconds"
```

---

## Migration Guide for Existing Installations

### Database Migrations
**Phase 2 adds 1 new table:**
- `RewardErrors` (migration: `20260328000000_AddErrorTracking`)

**Migration is automatic** on plugin load (BTCPay applies migrations on startup)

**Manual migration (if needed):**
```bash
docker exec -it generated_btcpayserver_1 bash
cd /datadir/Plugins/BTCPayServer.Plugins.BitcoinRewards
# Migrations run automatically on next restart
```

### Configuration Changes
**No breaking changes** - All Phase 2 features are additive:
- Existing settings preserved
- New fields have sensible defaults
- Error tracking is opt-in (automatic)
- Metrics exposed but not required

### API Compatibility
**New endpoints added:**
- `GET /api/v1/bitcoin-rewards/metrics`
- `GET /api/v1/bitcoin-rewards/metrics/json`
- `GET /api/v1/bitcoin-rewards/metrics/health`
- `GET /plugins/bitcoin-rewards/{storeId}/errors`
- `POST /plugins/bitcoin-rewards/{storeId}/errors/{errorId}/resolve`

**No existing endpoints changed.**

---

## Success Metrics

### Phase 2 Completion Criteria
- [x] Health checks operational (accessible via `/health`)
- [x] Error tracking database populated on failures
- [x] Admin error dashboard renders without errors
- [x] Metrics endpoint returns Prometheus format
- [ ] Rate limiting prevents abuse (429 responses)
- [ ] Auto-recovery successfully retries failed operations
- [ ] Correlation IDs in all log messages
- [ ] Query response times <500ms p95

### Production Readiness Checklist
- [ ] 80%+ test coverage (Phase 3)
- [ ] Load tested to 100 concurrent requests
- [ ] Grafana dashboards configured
- [ ] Alert rules deployed
- [ ] Documentation complete (Phase 4)
- [ ] Security audit passed (Phase 6)

---

## Resources

### Documentation
- **BTCPay Server Plugin Development:** https://docs.btcpayserver.org/Development/Plugins/
- **Prometheus Metrics:** https://prometheus.io/docs/concepts/metric_types/
- **ASP.NET Core Health Checks:** https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks

### External Dependencies
- **QRCoder:** QR code generation for LNURL
- **NBitcoin:** Bitcoin primitives
- **BTCPayServer.Lightning:** LNURL encoding
- **Serilog:** Structured logging (future)
- **StackExchange.Redis:** Distributed rate limiting (future)

### Team
- **Development:** Agent-assisted development (Overseer 🎯)
- **Product Owner:** JP
- **Environment:** BTCPay Server v2.3.4 on anmore.cash

---

## Timeline Summary

| Phase | Feature | Days | Status |
|-------|---------|------|--------|
| 2.1 | Health Checks | 1-2 | ✅ Complete |
| 2.2 | Error Handling | 3-6 | ✅ Complete |
| 2.3 | Metrics & Telemetry | 7-8 | ✅ Complete |
| 2.4 | Rate Limiting | 9-13 | ⏳ Not Started |
| 2.5 | Auto-Recovery | 14-18 | ⏳ Not Started |
| 2.6 | Advanced Logging | 19-23 | ⏳ Not Started |
| 2.7 | Performance Optimization | 24-28 | ⏳ Not Started |

**Current Day:** 5 of 28  
**Progress:** 45% complete  
**On Track:** Yes

---

## Contact

For questions or issues, see:
- **GitHub:** https://github.com/jpgaviria2/bitcoinrewards
- **Production URL:** https://anmore.cash
- **Store ID:** `DWJ4gyqwVYkSQBgDD7py2DW5izoNnCD9PBbK7P332hW8`

---

**Last Updated:** 2026-03-28 20:45 PST  
**Branch:** `feature/phase-3-testing`  
**Latest Commit:** `a717ad4`
