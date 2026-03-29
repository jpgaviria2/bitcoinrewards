# Bitcoin Rewards Plugin - API Reference

## Overview
This document describes all public APIs exposed by the Bitcoin Rewards plugin.

**Base URL:** `https://your-btcpay-domain.com`  
**Plugin Version:** 1.4.1  
**API Version:** v1

---

## Authentication

All admin endpoints require authentication via BTCPay Server's cookie-based auth or API keys.

**API Key Setup:**
1. BTCPay Server → Account → API Keys
2. Create new key with `btcpay.store.canmodifystoresettings` permission
3. Include in request: `Authorization: token YOUR_API_KEY`

---

## Endpoints

### Metrics API

#### Get Prometheus Metrics
```http
GET /api/v1/bitcoin-rewards/metrics
```

Returns Prometheus-format metrics for monitoring.

**Authentication:** None (public endpoint for Prometheus scraper)

**Response:**
```
Content-Type: text/plain; version=0.0.4; charset=utf-8

# HELP rewards_created_total Total number of rewards created
# TYPE rewards_created_total counter
rewards_created_total{platform="square",store="abc123"} 42

# HELP active_rewards Current number of active (unclaimed) rewards
# TYPE active_rewards gauge
active_rewards{store="abc123"} 5

# HELP reward_amount_satoshis Histogram of reward amounts in satoshis
# TYPE reward_amount_satoshis histogram
reward_amount_satoshis_bucket{le="1000"} 10
reward_amount_satoshis_bucket{le="5000"} 25
reward_amount_satoshis_bucket{le="+Inf"} 42
reward_amount_satoshis_sum 150000
reward_amount_satoshis_count 42
```

**Metrics Available:**
- `rewards_created_total` (counter) - Total rewards created
- `rewards_claimed_total` (counter) - Total rewards claimed
- `errors_total` (counter) - Total errors by type
- `webhooks_received_total` (counter) - Webhook requests received
- `lightning_operations_total` (counter) - Lightning operations (success/failure)
- `active_rewards` (gauge) - Currently active rewards
- `unclaimed_value_satoshis` (gauge) - Total unclaimed value
- `reward_amount_satoshis` (histogram) - Reward amount distribution
- `claim_duration_seconds` (histogram) - Time to claim rewards
- `webhook_duration_ms` (histogram) - Webhook processing latency

**Labels:**
- `platform` - square, shopify, btcpay
- `store` - Store ID
- `status` - pending, sent, claimed, expired
- `error_type` - webhook, lightning, api, database, business_logic
- `success` - true, false

---

#### Get JSON Metrics
```http
GET /api/v1/bitcoin-rewards/metrics/json
```

Returns metrics in JSON format for admin dashboards.

**Authentication:** Required (store admin)

**Response:**
```json
{
  "counters": {
    "rewards_created_total|platform=square|store=abc123": 42,
    "rewards_claimed_total|platform=square|store=abc123": 30
  },
  "gauges": {
    "active_rewards|store=abc123": 5,
    "unclaimed_value_satoshis|store=abc123": 25000
  },
  "histograms": {
    "reward_amount_satoshis": {
      "count": 42,
      "sum": 150000,
      "mean": 3571.43,
      "min": 1000,
      "max": 10000,
      "p50": 3000,
      "p95": 8500,
      "p99": 9800
    }
  }
}
```

**Status Codes:**
- `200` - Success
- `401` - Unauthorized (missing/invalid auth)
- `403` - Forbidden (insufficient permissions)

---

#### Get Metrics Health
```http
GET /api/v1/bitcoin-rewards/metrics/health
```

Returns health status of metrics system.

**Authentication:** None (public)

**Response:**
```json
{
  "status": "healthy",
  "totalMetrics": 15,
  "counters": 8,
  "gauges": 2,
  "histograms": 5
}
```

---

### Webhooks

#### Square Webhook
```http
POST /plugins/bitcoin-rewards/{storeId}/webhooks/square
```

Receives Square payment webhooks and creates rewards.

**Authentication:** Webhook signature validation (`X-Square-Signature` header)

**Request Headers:**
```
Content-Type: application/json
X-Square-Signature: base64-encoded-hmac-signature
```

**Request Body:**
```json
{
  "type": "payment.updated",
  "data": {
    "object": {
      "payment": {
        "id": "payment_123",
        "status": "COMPLETED",
        "amount_money": {
          "amount": 1000,
          "currency": "USD"
        },
        "receipt_email": "customer@example.com",
        "order_id": "order_123"
      }
    }
  }
}
```

**Response:**
```
200 OK
```

**Status Codes:**
- `200` - Webhook processed successfully
- `400` - Bad request (missing signature or invalid payload)
- `401` - Unauthorized (invalid signature)
- `429` - Too many requests (rate limited)
- `500` - Internal server error

**Rate Limits:**
- Default: 60 requests/minute per IP
- Configurable per store in settings

---

### Admin UI Endpoints

#### Error Dashboard
```http
GET /plugins/bitcoin-rewards/{storeId}/errors?days=7&resolved=false
```

Displays error dashboard with statistics and error list.

**Authentication:** Required (store admin)

**Query Parameters:**
- `days` (optional) - Number of days to show (1, 7, 30, 90). Default: 7
- `resolved` (optional) - Filter by resolution status (true, false, null for all)

**Response:** HTML page

---

#### Resolve Error
```http
POST /plugins/bitcoin-rewards/{storeId}/errors/{errorId}/resolve
```

Marks an error as resolved.

**Authentication:** Required (store admin)

**Response:** Redirect to error dashboard

---

#### Rate Limit Settings
```http
GET /plugins/bitcoin-rewards/{storeId}/rate-limits
```

Displays rate limit configuration UI.

**Authentication:** Required (store admin)

**Response:** HTML page

---

```http
POST /plugins/bitcoin-rewards/{storeId}/rate-limits
```

Saves rate limit configuration.

**Authentication:** Required (store admin)

**Request Body:** Form data
- `Enabled` - Enable/disable rate limiting
- `WebhookRequestsPerMinute` - Webhook rate limit
- `ApiRequestsPerMinute` - API rate limit
- `WhitelistedIpsText` - Whitelisted IPs (one per line)
- `BlacklistedIpsText` - Blacklisted IPs (one per line)

**Response:** Redirect to rate limit settings

---

#### Display Rewards Screen
```http
GET /plugins/bitcoin-rewards/{storeId}/display?timeframeMinutes=60&autoRefreshSeconds=10
```

Displays latest unclaimed reward on a customer-facing screen.

**Authentication:** None (public display)

**Query Parameters:**
- `timeframeMinutes` (optional) - How far back to search for rewards. Default: 60
- `autoRefreshSeconds` (optional) - Auto-refresh interval. Default: 10

**Response:** HTML page with reward details or waiting screen

---

## Rate Limiting

All plugin endpoints are subject to rate limiting to prevent abuse.

### Default Limits

| Endpoint Type | Requests/Minute | Burst |
|---------------|-----------------|-------|
| Webhooks      | 60              | 10    |
| API           | 120             | 20    |
| Admin UI      | 300             | 50    |

### Rate Limit Headers

Responses include rate limit information:

```http
X-RateLimit-Limit: 60
X-RateLimit-Remaining: 42
X-RateLimit-Reset: 1234567890
```

When rate limited:
```http
HTTP/1.1 429 Too Many Requests
Retry-After: 30

{
  "error": "Rate limit exceeded",
  "limitedBy": "IP",
  "retryAfter": 1234567890,
  "message": "Too many requests. Please try again after 2024-03-28T21:30:00Z"
}
```

---

## Error Responses

### Standard Error Format

```json
{
  "error": "Error type",
  "message": "Human-readable error message",
  "details": "Additional context (optional)"
}
```

### HTTP Status Codes

- `200` - Success
- `400` - Bad Request (invalid input)
- `401` - Unauthorized (missing authentication)
- `403` - Forbidden (insufficient permissions)
- `404` - Not Found
- `429` - Too Many Requests (rate limited)
- `500` - Internal Server Error
- `503` - Service Unavailable

---

## Webhooks (Outgoing)

*Future feature (Phase 5)*

The plugin will support webhooks to notify external systems when events occur.

**Planned Events:**
- `reward.created` - New reward created
- `reward.claimed` - Reward claimed by customer
- `reward.expired` - Reward expired (unclaimed)

---

## Examples

### Fetch Metrics with cURL

```bash
curl https://your-domain.com/api/v1/bitcoin-rewards/metrics
```

### Fetch JSON Metrics (Authenticated)

```bash
curl -H "Authorization: token YOUR_API_KEY" \
  https://your-domain.com/api/v1/bitcoin-rewards/metrics/json
```

### Prometheus Configuration

```yaml
scrape_configs:
  - job_name: 'btcpay-bitcoin-rewards'
    static_configs:
      - targets: ['your-domain.com']
    metrics_path: /api/v1/bitcoin-rewards/metrics
    scheme: https
    scrape_interval: 30s
```

### Test Webhook (Development Only)

```bash
curl -X POST \
  -H "Content-Type: application/json" \
  -H "X-Square-Signature: test_signature" \
  -d '{"type":"payment.updated","data":{"object":{"payment":{"id":"test","status":"COMPLETED","amount_money":{"amount":1000,"currency":"USD"}}}}}' \
  https://your-domain.com/plugins/bitcoin-rewards/YOUR_STORE_ID/webhooks/square/test
```

---

## SDKs and Libraries

### JavaScript/TypeScript

```typescript
// Fetch metrics
const response = await fetch('https://your-domain.com/api/v1/bitcoin-rewards/metrics/json', {
  headers: {
    'Authorization': 'token YOUR_API_KEY'
  }
});
const metrics = await response.json();
console.log(`Active rewards: ${metrics.gauges['active_rewards|store=YOUR_STORE']}`);
```

### Python

```python
import requests

# Fetch metrics
response = requests.get(
    'https://your-domain.com/api/v1/bitcoin-rewards/metrics/json',
    headers={'Authorization': 'token YOUR_API_KEY'}
)
metrics = response.json()
print(f"Active rewards: {metrics['gauges']['active_rewards|store=YOUR_STORE']}")
```

---

## Versioning

The API follows semantic versioning: `MAJOR.MINOR.PATCH`

**Current Version:** v1.4.1

**Breaking Changes:** Will increment MAJOR version  
**New Features:** Will increment MINOR version  
**Bug Fixes:** Will increment PATCH version

**Deprecation Policy:** Features will be deprecated for at least one MINOR version before removal.

---

## Support

- **Documentation:** https://github.com/jpgaviria2/bitcoinrewards/docs
- **Issues:** https://github.com/jpgaviria2/bitcoinrewards/issues
- **Discussions:** https://github.com/jpgaviria2/bitcoinrewards/discussions

---

**Last Updated:** 2026-03-28  
**Version:** 1.4.1
