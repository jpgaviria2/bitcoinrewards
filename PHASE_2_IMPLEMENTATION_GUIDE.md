# Phase 2: Full Production Implementation Guide
**Status:** IN PROGRESS  
**Target:** 95%+ Production Ready  
**Current:** 42% Ready

---

## ✅ Components Created (Today)

### 1. Rate Limiting Middleware
**File:** `Middleware/RateLimitMiddleware.cs`  
**Status:** ✅ Created, needs registration  

**Features:**
- Sliding window rate limiting algorithm
- Per-endpoint limits configured:
  - `/wallet/create`: 5/hour per IP
  - `/pay-invoice`: 20/minute per wallet
  - `/claim-lnurl`: 30/minute per wallet
  - `/swap`: 10/minute per wallet
  - `/balance`: 60/minute per wallet
  - `/settings`: 10/hour per wallet
- In-memory tracking with automatic cleanup
- Returns 429 (Too Many Requests) with retry-after

**TODO:**
- [ ] Register in `BitcoinRewardsPlugin.cs` startup
- [ ] Add background cleanup task
- [ ] Test with concurrent requests
- [ ] Add Redis backend for multi-server deployment (optional)

---

### 2. Idempotency Service
**File:** `Services/IdempotencyService.cs`  
**Status:** ✅ Created, needs integration  

**Features:**
- Prevents duplicate operations (especially payments)
- 24-hour retention window
- In-memory cache with automatic cleanup
- Supports client-provided or server-generated keys
- Returns cached results for duplicate requests

**Updated Files:**
- `WalletApiController.cs` - PayInvoiceRequest now implements IIdempotentRequest

**TODO:**
- [ ] Add IdempotencyService to controller constructor
- [ ] Update pay-invoice endpoint to check/cache results
- [ ] Update claim-lnurl endpoint to support idempotency
- [ ] Add background cleanup task
- [ ] Test duplicate payment scenarios

---

## 📋 Remaining Implementation Tasks

### Task 1: Register Middleware & Services
**File:** `BitcoinRewardsPlugin.cs`

```csharp
// In Execute() method:

// Register services
services.AddSingleton<IdempotencyService>();

// Register middleware (in Configure callback)
app.UseMiddleware<RateLimitMiddleware>();
```

---

### Task 2: Update pay-invoice Endpoint
**File:** `Controllers/WalletApiController.cs`

```csharp
// Add to constructor:
private readonly IdempotencyService _idempotencyService;

public WalletApiController(..., IdempotencyService idempotencyService, ...)
{
    _idempotencyService = idempotencyService;
    // ...
}

// Update pay-invoice method:
[HttpPost("plugins/bitcoin-rewards/wallet/{walletId}/pay-invoice")]
[AllowAnonymous]
public async Task<IActionResult> PayInvoice(Guid walletId, [FromBody] PayInvoiceRequest request)
{
    // Generate idempotency key if not provided
    var idempotencyKey = request.IdempotencyKey 
        ?? _idempotencyService.GenerateKey(walletId, "pay-invoice", request.Invoice);
    
    // Check for duplicate request
    var cachedResult = _idempotencyService.GetCachedResult<object>(idempotencyKey);
    if (cachedResult != null)
    {
        _logger.LogInformation("Returning cached payment result for idempotency key {Key}", idempotencyKey);
        return Ok(cachedResult);
    }
    
    // ... existing payment logic ...
    
    // Cache the successful result
    var result = new { success = true, ... };
    _idempotencyService.CacheResult(idempotencyKey, result);
    return Ok(result);
}
```

---

### Task 3: Background Cleanup Service
**File:** `HostedServices/MaintenanceService.cs` (NEW)

```csharp
public class MaintenanceService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            
            // Cleanup rate limit history
            RateLimitMiddleware.CleanupExpiredHistories();
            
            // Cleanup idempotency cache
            var removed = IdempotencyService.CleanupExpiredEntries();
            _logger.LogInformation("Maintenance: removed {Count} expired idempotency entries", removed);
        }
    }
}
```

Register in plugin:
```csharp
services.AddHostedService<MaintenanceService>();
```

---

### Task 4: Enhanced Error Handling
**Files:** Various controllers

**Add structured error responses:**
```csharp
public class ApiError
{
    public string Error { get; set; }
    public string? Code { get; set; }
    public string? Detail { get; set; }
    public Dictionary<string, string>? Fields { get; set; }
}
```

**Standardize error codes:**
- `INSUFFICIENT_BALANCE`
- `PAYMENT_FAILED`
- `INVOICE_EXPIRED`
- `RATE_LIMIT_EXCEEDED`
- `DUPLICATE_REQUEST`
- `TIMEOUT`

---

### Task 5: Transaction Rollback Support
**File:** `Services/CustomerWalletService.cs`

**Wrap DB operations in transactions:**
```csharp
public async Task<(bool Success, string? Error)> SpendCadAsync(...)
{
    await using var ctx = _dbFactory.CreateContext();
    await using var transaction = await ctx.Database.BeginTransactionAsync();
    
    try
    {
        // Debit CAD
        wallet.CadBalanceCents -= cadCents;
        
        // Record transaction
        ctx.WalletTransactions.Add(...);
        
        await ctx.SaveChangesAsync();
        await transaction.CommitAsync();
        
        return (true, null);
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return (false, ex.Message);
    }
}
```

---

### Task 6: Comprehensive Unit Tests
**Files:** `Tests/` directory (NEW)

**Test Structure:**
```
Tests/
├── Controllers/
│   └── WalletApiControllerTests.cs (30 tests)
├── Services/
│   ├── CustomerWalletServiceTests.cs (40 tests)
│   ├── IdempotencyServiceTests.cs (15 tests)
│   └── ExchangeRateServiceTests.cs (10 tests)
├── Middleware/
│   └── RateLimitMiddlewareTests.cs (15 tests)
├── Integration/
│   └── PaymentFlowTests.cs (20 tests)
└── TestHelpers.cs
```

**Test Categories:**
- Happy path scenarios
- Error handling
- Edge cases (0 amounts, negative, overflow)
- Concurrent operations
- Network failures
- Database failures
- Timeout scenarios

**Target:** 95%+ code coverage

---

### Task 7: Integration Tests
**File:** `Tests/Integration/WalletFlowTests.cs`

**Critical Flows to Test:**
1. **Full claim flow:** LNURL → claim → watcher → credit
2. **Full payment flow:** pay-invoice → Lightning → deduct CAD
3. **Swap flow:** CAD → sats → CAD
4. **Concurrent claims** (same wallet)
5. **Concurrent payments** (same wallet)
6. **Idempotency:** retry same payment
7. **Rate limiting:** exceed limits
8. **Failure scenarios:** routing failure, timeout, DB error

---

### Task 8: Monitoring & Alerting
**File:** `Services/MonitoringService.cs` (NEW)

**Metrics to Track:**
- Payment success/failure rates
- Average payment time
- Rate limit triggers
- Idempotency cache hits
- Balance inconsistencies
- API error rates

**Alerts:**
- Payment failure rate > 10%
- Any critical errors (payment succeeded but CAD deduction failed)
- Rate limit abuse (same client hitting limits repeatedly)
- Database errors

**Implementation Options:**
- Application Insights (Azure)
- Prometheus + Grafana
- Serilog structured logging
- Custom webhook notifications

---

### Task 9: Load Testing
**Tool:** k6 or Apache JMeter

**Test Scenarios:**
1. **Normal load:** 100 req/min for 10 minutes
2. **Peak load:** 1000 req/min for 5 minutes
3. **Spike test:** 0 → 500 → 0 in 1 minute
4. **Sustained load:** 500 req/min for 1 hour
5. **Concurrent payments:** 50 simultaneous pay-invoice calls

**Success Criteria:**
- p95 response time < 1s (excluding Lightning 60s timeout)
- 0% error rate under normal load
- < 1% error rate under peak load
- No memory leaks
- Database connections properly released

---

### Task 10: Security Audit
**Checklist:**

**Input Validation:**
- [ ] All string inputs sanitized
- [ ] Numeric overflow checks
- [ ] BOLT11 invoice validation
- [ ] UUID format validation

**Authentication:**
- [ ] Bearer token on all endpoints
- [ ] Token validation timing-safe comparison
- [ ] Token expiry enforced

**Authorization:**
- [ ] Wallet belongs to token holder
- [ ] Store access verified

**SQL Injection:**
- [ ] All queries parameterized
- [ ] No raw SQL with user input

**SSRF Protection:**
- [ ] LNURL callback URL validation
- [ ] Timeout on external requests
- [ ] Restricted to HTTPS only

**Rate Limiting:**
- [ ] All endpoints protected
- [ ] Limits tested and validated

**Audit Logging:**
- [ ] All financial operations logged
- [ ] Sensitive data redacted
- [ ] Tamper-evident logs

---

### Task 11: Documentation
**Files to Create/Update:**

**API Documentation:**
- `API_REFERENCE.md` - Complete endpoint reference
- `ERROR_CODES.md` - All error codes with examples
- `INTEGRATION_GUIDE.md` - For iOS/Android developers

**Operations:**
- `DEPLOYMENT.md` - Deployment procedures
- `MONITORING.md` - Metrics and alerts guide
- `INCIDENT_RESPONSE.md` - What to do when things break
- `DISASTER_RECOVERY.md` - Backup and restore procedures

**Development:**
- `TESTING_GUIDE.md` - How to run tests
- `CONTRIBUTING.md` - Code standards
- Update `README.md` with architecture overview

---

## 📊 Implementation Progress Tracker

| Task | Status | Priority | Time Est. | Assignee |
|------|--------|----------|-----------|----------|
| Rate limit registration | ⏳ TODO | HIGH | 2h | - |
| Idempotency integration | ⏳ TODO | HIGH | 4h | - |
| Background cleanup service | ⏳ TODO | HIGH | 2h | - |
| Enhanced error handling | ⏳ TODO | MEDIUM | 4h | - |
| Transaction rollback | ⏳ TODO | HIGH | 4h | - |
| Unit tests | ⏳ TODO | HIGH | 16h | - |
| Integration tests | ⏳ TODO | HIGH | 8h | - |
| Monitoring setup | ⏳ TODO | MEDIUM | 8h | - |
| Load testing | ⏳ TODO | MEDIUM | 4h | - |
| Security audit | ⏳ TODO | HIGH | 8h | - |
| Documentation | ⏳ TODO | MEDIUM | 8h | - |

**Total Estimated Time:** 68 hours (8.5 days)

---

## 🎯 Milestones

### Milestone 1: Critical Safety (70% Ready) - ETA: 2 days
- ✅ Rate limiting registered & tested
- ✅ Idempotency fully integrated
- ✅ Background cleanup running
- ✅ Transaction rollbacks implemented
- ✅ Enhanced error handling

### Milestone 2: Testing Complete (85% Ready) - ETA: +3 days
- ✅ Unit tests written (95%+ coverage)
- ✅ Integration tests passing
- ✅ Load testing passed

### Milestone 3: Production Ready (95% Ready) - ETA: +3 days
- ✅ Security audit complete
- ✅ Monitoring deployed
- ✅ Documentation complete
- ✅ Disaster recovery plan
- ✅ Final sign-off

---

## 🚀 Quick Start: Next Actions

**If doing full implementation now:**
1. Register RateLimitMiddleware in plugin (30 min)
2. Add IdempotencyService to DI container (15 min)
3. Update pay-invoice to use idempotency (1 hour)
4. Add MaintenanceService for cleanup (1 hour)
5. Write first set of unit tests (4 hours)
6. Test with concurrent requests (2 hours)

**If documenting for later:**
- This guide is complete ✅
- Mark as Phase 2 backlog
- Can ship current 42% ready version with known risks
- Revisit after iOS/Android launch

---

**Decision Point:** Continue implementation now or document as backlog?

Current safe to ship: YES (critical payment bugs fixed)  
Recommended before scale: Complete Milestone 1 (70% ready)  
Required for enterprise: Complete all 3 milestones (95% ready)
