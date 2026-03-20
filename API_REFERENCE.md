# Bitcoin Rewards Plugin - API Reference

**Version:** 2.0 (Production Hardening)  
**Base URL:** `https://your-btcpay-server.com/plugins/bitcoin-rewards`  
**Authentication:** Bearer token in `Authorization` header

---

## Table of Contents

1. [Wallet Management](#wallet-management)
2. [Payment Operations](#payment-operations)
3. [LNURL Claims](#lnurl-claims)
4. [Balance & Swap](#balance--swap)
5. [Error Codes](#error-codes)
6. [Rate Limits](#rate-limits)
7. [Idempotency](#idempotency)

---

## Wallet Management

### Create Wallet

**POST** `/wallet/create`

Creates a new customer wallet with dual CAD + sats balance.

**Request:**
```json
{
  "storeId": "9TipzyZe9J2RYjQNXeGyr9FRuzjBijYZCo2YA4ggsr1c",
  "autoConvertToCad": true
}
```

**Response (200 OK):**
```json
{
  "walletId": "28a167b4-d6de-4e02-bfb2-50fde7282829",
  "walletToken": "base64-encoded-token",
  "cadBalanceCents": 0,
  "satsBalance": 0,
  "pullPaymentId": "btcpay-pull-payment-id"
}
```

**Rate Limit:** 5 requests/hour per IP

**Error Codes:**
- `WALLET_CREATION_FAILED` - Internal error during wallet creation
- `RATE_LIMIT_EXCEEDED` - Too many wallet creation attempts

---

### Get Balance

**GET** `/wallet/{walletId}/balance`

Retrieves current wallet balance (CAD cents + sats).

**Headers:**
```
Authorization: Bearer {walletToken}
```

**Response (200 OK):**
```json
{
  "walletId": "28a167b4-d6de-4e02-bfb2-50fde7282829",
  "cadBalanceCents": 1900,
  "satsBalance": 20000,
  "autoConvertToCad": true
}
```

**Rate Limit:** 60 requests/minute per wallet

**Error Codes:**
- `WALLET_NOT_FOUND` - Wallet ID not found
- `WALLET_TOKEN_INVALID` - Invalid or missing bearer token

---

## Payment Operations

### Pay Lightning Invoice

**POST** `/wallet/{walletId}/pay-invoice`

Pay a BOLT11 Lightning invoice from CAD balance. Payment is **synchronous** - endpoint waits for Lightning confirmation before deducting balance.

**Headers:**
```
Authorization: Bearer {walletToken}
Content-Type: application/json
Idempotency-Key: client-generated-uuid (optional)
```

**Request:**
```json
{
  "invoice": "lnbc100u1...",
  "idempotencyKey": "550e8400-e29b-41d4-a716-446655440000"
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "cadCentsCharged": 150,
  "satsAmount": 10000,
  "exchangeRate": 0.015,
  "newCadBalanceCents": 1750,
  "newSatsBalance": 20000,
  "paymentHash": "abc123...",
  "preimage": "def456..."
}
```

**Rate Limit:** 20 requests/minute per wallet

**Timeout:** 60 seconds (waits for Lightning payment)

**Idempotency:** Duplicate requests (same invoice + wallet) return cached result for 24 hours

**Error Codes:**
- `INVOICE_INVALID` - Malformed BOLT11 invoice
- `INVOICE_EXPIRED` - Invoice expiry date passed
- `INVOICE_NO_AMOUNT` - Invoice has no amount
- `INSUFFICIENT_CAD_BALANCE` - Not enough CAD to pay invoice
- `PAYMENT_FAILED` - Lightning payment failed
- `PAYMENT_ROUTING_FAILED` - No route found
- `PAYMENT_TIMEOUT` - Payment took > 60 seconds
- `DUPLICATE_REQUEST` - Idempotent request, returning cached result

**Critical Feature:** Payment confirmation is **synchronous**. CAD is only deducted AFTER Lightning payment succeeds. If payment fails, no charge occurs.

---

## LNURL Claims

### Claim LNURL-Withdraw Reward

**POST** `/wallet/{walletId}/claim-lnurl`

Claims an LNURL-withdraw reward (from BTCPay rewards terminal or Android app).

**Headers:**
```
Authorization: Bearer {walletToken}
Content-Type: application/json
```

**Request:**
```json
{
  "callback": "https://btcpay.server/lnurl/...",
  "k1": "secret-key",
  "amount": 147000,
  "description": "Coffee purchase reward"
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "cadCents": 22,
  "satsAmount": 1470,
  "exchangeRate": 0.014965,
  "newCadBalanceCents": 1922,
  "newSatsBalance": 20000
}
```

**Rate Limit:** 30 requests/minute per wallet

**Error Codes:**
- `LNURL_CALLBACK_FAILED` - LNURL service didn't accept the claim
- `LNURL_ALREADY_CLAIMED` - Reward already claimed
- `BALANCE_UPDATE_FAILED` - Payment succeeded but balance update failed (user still gets paid)

**Note:** If `autoConvertToCad` is ON, sats are converted to CAD at current exchange rate. If OFF, sats are credited directly.

---

## Balance & Swap

### Swap CAD → Sats

**POST** `/wallet/{walletId}/swap`

Converts CAD balance to sats at current exchange rate.

**Headers:**
```
Authorization: Bearer {walletToken}
```

**Request:**
```json
{
  "cadCents": 500,
  "direction": "to-sats"
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "cadCentsSpent": 500,
  "satsReceived": 33333,
  "exchangeRate": 0.015,
  "newCadBalanceCents": 1400,
  "newSatsBalance": 53333
}
```

**Rate Limit:** 10 requests/minute per wallet

**Error Codes:**
- `INSUFFICIENT_CAD_BALANCE` - Not enough CAD for swap
- `SWAP_RATE_UNAVAILABLE` - Exchange rate not available
- `CONVERSION_ZERO_RESULT` - Amount too small, results in zero sats

---

### Swap Sats → CAD

**POST** `/wallet/{walletId}/swap`

Converts sats balance to CAD at current exchange rate.

**Request:**
```json
{
  "satsAmount": 10000,
  "direction": "to-cad"
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "satsSpent": 10000,
  "cadCentsReceived": 150,
  "exchangeRate": 0.015,
  "newCadBalanceCents": 1550,
  "newSatsBalance": 43333
}
```

**Error Codes:**
- `INSUFFICIENT_SATS_BALANCE` - Not enough sats for swap
- `SWAP_RATE_UNAVAILABLE` - Exchange rate not available

---

## Error Codes

All error responses follow this format:

```json
{
  "error": "Human-readable error message",
  "code": "MACHINE_READABLE_CODE",
  "detail": "Technical details (optional)",
  "retryAfterSeconds": 60
}
```

### HTTP Status Codes

- `200 OK` - Success
- `400 Bad Request` - Invalid input (check `code` field)
- `401 Unauthorized` - Missing or invalid bearer token
- `404 Not Found` - Wallet not found
- `429 Too Many Requests` - Rate limit exceeded (see `Retry-After` header)
- `500 Internal Server Error` - Server error
- `503 Service Unavailable` - Temporary outage

### Error Code Categories

**1000-1999:** Wallet errors  
**2000-2999:** Balance errors  
**3000-3999:** Payment errors  
**4000-4999:** Invoice errors  
**5000-5999:** LNURL errors  
**6000-6999:** Swap errors  
**7000-7999:** Rate limiting  
**8000-8999:** Exchange rate errors  
**9000-9999:** System errors  
**10000-10999:** Validation errors  
**11000-11999:** Bolt Card errors  
**12000-12999:** Idempotency errors  

See `Models/ApiError.cs` for complete error code list.

---

## Rate Limits

Rate limits are enforced per-endpoint to prevent abuse:

| Endpoint | Limit | Scope |
|----------|-------|-------|
| `/wallet/create` | 5/hour | Per IP address |
| `/wallet/{id}/balance` | 60/minute | Per wallet |
| `/wallet/{id}/pay-invoice` | 20/minute | Per wallet |
| `/wallet/{id}/claim-lnurl` | 30/minute | Per wallet |
| `/wallet/{id}/swap` | 10/minute | Per wallet |
| `/wallet/{id}/settings` | 10/hour | Per wallet |
| `/wallet/{id}/history` | 30/minute | Per wallet |

**Rate Limit Response (429):**
```json
{
  "error": "Rate limit exceeded",
  "code": "RATE_LIMIT_EXCEEDED",
  "retryAfterSeconds": 42
}
```

**Header:** `Retry-After: 42` (seconds until limit resets)

**Algorithm:** Sliding window - limits apply to the last N minutes/hours, not fixed time windows.

---

## Idempotency

### Why Idempotency Matters

Network failures and retries can cause duplicate requests. Idempotency keys prevent:
- Double-charging on `pay-invoice` retries
- Duplicate LNURL claims
- Accidental double-swaps

### How It Works

1. **Client provides key** (recommended):
   ```
   Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
   ```

2. **Server generates key** (fallback):
   - For `pay-invoice`: SHA256(walletId + invoice)
   - For `claim-lnurl`: SHA256(walletId + k1 + amount)

3. **Caching:**
   - Successful responses cached for **24 hours**
   - Duplicate requests return cached response (200 OK with original result)
   - Failed requests are NOT cached (client can retry with same key)

### Best Practices

- **Use UUIDs** for idempotency keys (high entropy)
- **Retry logic:** Use same key for all retries of the same operation
- **Timeouts:** If request times out, retry with the same key
- **New operations:** Generate new key for each unique operation

### Example Flow

```javascript
const idempotencyKey = uuidv4();

try {
  const response = await fetch('/wallet/123/pay-invoice', {
    method: 'POST',
    headers: {
      'Authorization': 'Bearer token',
      'Idempotency-Key': idempotencyKey
    },
    body: JSON.stringify({ invoice: 'lnbc...' })
  });
  
  if (response.status === 200) {
    // Success (could be original or cached response)
    const data = await response.json();
    console.log('Payment completed:', data);
  }
} catch (error) {
  // Network error - safe to retry with same key
  // Server will return cached result if payment succeeded
}
```

---

## Testing

### Sandbox Environment

**URL:** `https://btcpay.anmore.me/plugins/bitcoin-rewards`

Test wallets available for development. Contact support for test credentials.

### Example cURL Requests

**Create wallet:**
```bash
curl -X POST https://btcpay.anmore.me/plugins/bitcoin-rewards/wallet/create \
  -H "Content-Type: application/json" \
  -d '{"storeId":"9TipzyZe9J2RYjQNXeGyr9FRuzjBijYZCo2YA4ggsr1c","autoConvertToCad":true}'
```

**Pay invoice:**
```bash
curl -X POST https://btcpay.anmore.me/plugins/bitcoin-rewards/wallet/{walletId}/pay-invoice \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $(uuidgen)" \
  -d '{"invoice":"lnbc100u1..."}'
```

---

## Support

**Repository:** https://github.com/jpgaviria2/bitcoinrewards  
**Issues:** https://github.com/jpgaviria2/bitcoinrewards/issues  
**Documentation:** See repository README and production guides

**Version History:**
- **2.0** - Production hardening (idempotency, rate limiting, transaction rollback)
- **1.2** - Synchronous payment confirmation fix
- **1.0** - Initial dual-balance wallet release
