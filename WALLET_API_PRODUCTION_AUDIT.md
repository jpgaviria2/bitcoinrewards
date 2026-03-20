# Wallet API Production Audit & Testing Plan
**Date:** 2026-03-20  
**Status:** IN PROGRESS  
**Target:** Production-ready with 95%+ test coverage

## Critical Issues Found

### 🔴 CRITICAL: pay-invoice Returns Success Before Payment Confirmed
**Issue:** `/wallet/{id}/pay-invoice` deducts CAD and returns success before Lightning payment actually completes.  
**Impact:** User loses money if payment fails (routing issues, timeouts, etc.)  
**Status:** FIXING NOW  
**Fix:** Implement synchronous payment with actual Lightning confirmation

---

## API Endpoints Audit

### ✅ POST `/wallet/create`
**Status:** FIXED (2026-03-20)  
**Issues Found:**
- ❌ Was creating wallets with non-existent pull payments
- ✅ Now creates actual BTCPay pull payments

**Edge Cases to Test:**
- [x] Invalid storeId
- [x] Store doesn't exist
- [x] Lightning not configured for store
- [ ] Concurrent wallet creation requests
- [ ] Pull payment creation fails
- [ ] Database transaction rollback

**Security:**
- [ ] Rate limiting (prevent spam wallet creation)
- [ ] Input validation (storeId format)
- [ ] Token generation entropy check

---

### ⚠️ GET `/wallet/{id}/balance`
**Status:** NEEDS REVIEW  
**Issues Found:**
- None yet

**Edge Cases to Test:**
- [ ] Wallet doesn't exist
- [ ] Invalid wallet ID format
- [ ] Invalid bearer token
- [ ] Wallet exists but pull payment deleted
- [ ] Concurrent balance queries during transaction
- [ ] Negative balances (should never happen)

**Security:**
- [ ] Token validation
- [ ] Authorization (wallet belongs to token)
- [ ] SQL injection in wallet ID

---

### 🔴 POST `/wallet/{id}/pay-invoice` 
**Status:** CRITICAL - REWRITING NOW  
**Issues Found:**
- ❌ Returns success before payment confirmed
- ❌ No payment confirmation wait
- ❌ No rollback if payment fails
- ❌ Creates payout but doesn't verify it sent

**Edge Cases to Test:**
- [ ] Invalid BOLT11 invoice
- [ ] Expired invoice
- [ ] Invoice with no amount
- [ ] Invoice amount > CAD balance
- [ ] Lightning routing failure
- [ ] Payment timeout (30s+)
- [ ] Concurrent payment requests (same wallet)
- [ ] Payment succeeds but DB update fails
- [ ] Payment fails after CAD deducted

**Security:**
- [ ] Amount overflow check
- [ ] Invoice validation (not malformed)
- [ ] Prevent double-payment (idempotency)
- [ ] Timeout limits (prevent DOS)

---

### ⚠️ POST `/wallet/{id}/claim-lnurl`
**Status:** NEEDS REVIEW  
**Issues Found:**
- Works asynchronously (watcher credits later)
- Might have similar issues to pay-invoice

**Edge Cases to Test:**
- [ ] Invalid LNURL callback
- [ ] Invalid k1
- [ ] Amount out of bounds
- [ ] LNURL service down/timeout
- [ ] LNURL callback returns error
- [ ] Invoice created but never paid
- [ ] Duplicate claim attempts
- [ ] Watcher fails to credit
- [ ] Database tracking fails

**Security:**
- [ ] LNURL callback SSRF protection
- [ ] Timeout on LNURL callback
- [ ] Validate k1 format
- [ ] Rate limiting per wallet

---

### ⚠️ POST `/wallet/{id}/swap`
**Status:** NEEDS REVIEW  
**Issues Found:**
- None yet

**Edge Cases to Test:**
- [ ] Swap 0 amount
- [ ] Swap negative amount
- [ ] Swap more than balance
- [ ] Concurrent swaps
- [ ] Exchange rate changes mid-swap
- [ ] Direction not 'to_cad' or 'to_sats'
- [ ] Pull payment full (can't top up more sats)
- [ ] Swap fails but balance already deducted

**Security:**
- [ ] Amount overflow
- [ ] Exchange rate manipulation
- [ ] Atomic swap (rollback on failure)

---

### ✅ POST `/wallet/{id}/settings`
**Status:** OK  
**Edge Cases to Test:**
- [ ] Invalid autoConvert value
- [ ] Concurrent settings updates

---

### ✅ GET `/wallet/{id}/history`
**Status:** OK  
**Edge Cases to Test:**
- [ ] Empty history
- [ ] Very large history (10k+ transactions)
- [ ] Pagination needed?

---

## Database Schema Audit

### CustomerWallets Table
- [x] SatsBalanceSatoshis added (2026-03-20)
- [ ] Foreign key constraints on PullPaymentId?
- [ ] Index on StoreId for performance
- [ ] Soft delete vs hard delete

### WalletTransactions Table
- [ ] Should track pending vs completed states?
- [ ] Index on CustomerWalletId, CreatedAt
- [ ] Transaction rollback support?

### PendingLnurlClaims Table
- [x] Created for watcher tracking
- [ ] Cleanup of expired/completed claims
- [ ] Index on CreatedAt for cleanup queries

### BoltCardLinks Table
- [x] Columns fixed (2026-03-19)
- [ ] Indexes reviewed
- [ ] Orphan cleanup process

---

## Error Handling Matrix

| Scenario | Current Behavior | Expected Behavior | Status |
|----------|------------------|-------------------|--------|
| Lightning routing fails | ❌ Returns success | ❌ Return error, refund | BROKEN |
| Pull payment not found | ❌ Varies | ❌ Clear error message | NEEDS FIX |
| Insufficient balance | ✅ Clear error | ✅ Clear error | OK |
| Invalid invoice | ✅ Clear error | ✅ Clear error | OK |
| Database timeout | ❓ Unknown | ❌ Retry + error | UNKNOWN |
| Network timeout | ❓ Unknown | ❌ Timeout error | UNKNOWN |
| Concurrent requests | ❓ Unknown | ✅ Serialized | UNKNOWN |

---

## Security Checklist

- [ ] **Input Validation:** All string inputs sanitized
- [ ] **SQL Injection:** Parameterized queries only
- [ ] **Auth Bypass:** Token validation on all endpoints
- [ ] **Rate Limiting:** Prevent spam/DOS
- [ ] **Amount Overflow:** Check all numeric operations
- [ ] **SSRF:** Validate all external URLs (LNURL callbacks)
- [ ] **Timing Attacks:** Constant-time token comparison
- [ ] **Replay Attacks:** Idempotency keys for payments
- [ ] **Transaction Atomicity:** Rollback on failure
- [ ] **Audit Logging:** All financial operations logged

---

## Performance Benchmarks

- [ ] `/wallet/create`: < 500ms (p95)
- [ ] `/wallet/{id}/balance`: < 100ms (p95)
- [ ] `/wallet/{id}/pay-invoice`: < 30s (Lightning timeout)
- [ ] `/wallet/{id}/claim-lnurl`: < 5s (LNURL callback timeout)
- [ ] `/wallet/{id}/swap`: < 200ms (p95)
- [ ] `/wallet/{id}/history`: < 500ms with 1000 transactions

---

## Unit Test Coverage Plan

### WalletApiController Tests
- [ ] CreateWallet: 10 test cases
- [ ] GetBalance: 8 test cases
- [ ] PayInvoice: 20 test cases (critical)
- [ ] ClaimLnurl: 15 test cases
- [ ] Swap: 12 test cases
- [ ] Settings: 6 test cases
- [ ] History: 8 test cases

**Total: ~80 test cases for controller**

### CustomerWalletService Tests
- [ ] GetOrCreateWalletAsync: 8 tests
- [ ] GetBalanceAsync: 6 tests
- [ ] CreditSatsAsync: 10 tests
- [ ] CreditCadAsync: 10 tests
- [ ] SpendSatsAsync: 10 tests
- [ ] SpendCadAsync: 10 tests
- [ ] SwapToCadAsync: 12 tests
- [ ] SwapToSatsAsync: 12 tests

**Total: ~80 test cases for service**

### Integration Tests
- [ ] Full payment flow (claim → swap → pay)
- [ ] Concurrent operations
- [ ] Database failure scenarios
- [ ] Lightning network failures
- [ ] Race conditions

**Total: ~20 integration tests**

**Grand Total: 180+ tests for 95%+ coverage**

---

## Rollout Plan

### Phase 1: Critical Fixes (TODAY)
- [x] Fix wallet/create pull payment issue
- [ ] Fix pay-invoice synchronous confirmation
- [ ] Add transaction rollback support
- [ ] Deploy to wallet server (btcpay.anmore.me)

### Phase 2: Comprehensive Testing (NEXT)
- [ ] Write all unit tests
- [ ] Run integration tests
- [ ] Load testing (100 concurrent users)
- [ ] Chaos testing (network failures, DB crashes)

### Phase 3: Production Hardening (BEFORE LAUNCH)
- [ ] Add rate limiting
- [ ] Add retry logic with exponential backoff
- [ ] Add circuit breakers for external services
- [ ] Add comprehensive logging
- [ ] Add metrics/monitoring
- [ ] Add alerting for failures

### Phase 4: Documentation
- [ ] API documentation with all edge cases
- [ ] Error code reference
- [ ] Deployment guide
- [ ] Disaster recovery procedures

---

## Sign-off Criteria

- ✅ All critical bugs fixed
- ✅ 95%+ test coverage achieved
- ✅ All edge cases tested
- ✅ Load testing passed (1000 req/min)
- ✅ Security audit passed
- ✅ Performance benchmarks met
- ✅ Documentation complete
- ✅ Monitoring in place

**Production-ready when all above are ✅**
