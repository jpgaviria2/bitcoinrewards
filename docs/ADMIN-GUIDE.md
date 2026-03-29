# Bitcoin Rewards Plugin - Administrator Guide

## For System Administrators and DevOps

This guide covers deployment, monitoring, troubleshooting, and maintenance of the Bitcoin Rewards plugin in production environments.

---

## Table of Contents

1. [System Requirements](#system-requirements)
2. [Installation & Deployment](#installation--deployment)
3. [Database Management](#database-management)
4. [Monitoring & Observability](#monitoring--observability)
5. [Performance Tuning](#performance-tuning)
6. [Security Hardening](#security-hardening)
7. [Backup & Recovery](#backup--recovery)
8. [Troubleshooting](#troubleshooting)

---

## System Requirements

### Minimum Requirements

- **BTCPay Server:** v2.3.0 or later
- **Operating System:** Linux (Ubuntu 20.04+, Debian 11+)
- **Database:** PostgreSQL 13+
- **Memory:** 2GB RAM (4GB recommended)
- **Storage:** 10GB free space
- **Network:** HTTPS-enabled domain

### Lightning Node Requirements

- **LND:** v0.15.0+ (recommended: v0.17.0+)
- **Core Lightning:** v23.08+ (alternative)
- **Channel Capacity:** Minimum 5,000,000 sats for rewards
- **Liquidity:** Maintain inbound/outbound balance

### Optional Components

- **Prometheus:** For metrics collection
- **Grafana:** For metrics visualization
- **Redis:** For distributed rate limiting (multi-instance deployments)

---

## Installation & Deployment

### Production Deployment

#### 1. Plugin Installation

```bash
# Navigate to BTCPay plugins directory
cd /var/lib/docker/volumes/generated_btcpay_datadir/_data/Plugins

# Download latest release
wget https://github.com/jpgaviria2/bitcoinrewards/releases/latest/download/BTCPayServer.Plugins.BitcoinRewards.zip

# Extract
unzip BTCPayServer.Plugins.BitcoinRewards.zip -d BTCPayServer.Plugins.BitcoinRewards/

# Set permissions
chown -R btcpay:btcpay BTCPayServer.Plugins.BitcoinRewards/

# Restart BTCPay Server
docker restart generated_btcpayserver_1
```

#### 2. Verify Installation

```bash
# Check plugin loaded successfully
docker logs generated_btcpayserver_1 | grep "BitcoinRewards"

# Should see:
# [INFO] Plugin loaded: BTCPayServer.Plugins.BitcoinRewards v1.4.1
```

#### 3. Database Migration

Database migrations run automatically on first startup.

**Verify migrations:**
```sql
SELECT * FROM "__EFMigrationsHistory" 
WHERE "MigrationId" LIKE '%BitcoinRewards%'
ORDER BY "MigrationId";
```

**Expected migrations:**
- `20260328000000_AddErrorTracking` - RewardErrors table
- Additional migrations as plugin evolves

#### 4. Configuration

**Environment Variables** (optional):
```bash
# In docker-compose.yml or BTCPay env file
BTCPAY_BITCOINREWARDS_LOGLEVEL=Information
BTCPAY_BITCOINREWARDS_CACHE_SIZE=1000
BTCPAY_BITCOINREWARDS_RATE_LIMIT_ENABLED=true
```

---

## Database Management

### Tables Created

```sql
-- Rewards table (from original plugin)
BitcoinRewards (
    Id UUID PRIMARY KEY,
    StoreId VARCHAR NOT NULL,
    TransactionId VARCHAR NOT NULL,
    RewardAmountSatoshis BIGINT NOT NULL,
    Status INT NOT NULL,
    CreatedAt TIMESTAMP NOT NULL,
    ...
)

-- Error tracking (Phase 2.2)
RewardErrors (
    Id SERIAL PRIMARY KEY,
    StoreId VARCHAR NOT NULL,
    RewardId VARCHAR,
    Operation VARCHAR NOT NULL,
    ErrorMessage TEXT NOT NULL,
    IsRetryable BOOLEAN NOT NULL,
    RetryCount INT NOT NULL DEFAULT 0,
    CreatedAt TIMESTAMP NOT NULL,
    ...
)
```

### Indexes

**Performance-critical indexes:**
```sql
-- BitcoinRewards table
CREATE INDEX IX_BitcoinRewards_StoreId_Status_CreatedAt 
    ON "BitcoinRewards" ("StoreId", "Status", "CreatedAt" DESC);

CREATE INDEX IX_BitcoinRewards_TransactionId_Platform 
    ON "BitcoinRewards" ("TransactionId", "Platform");

-- RewardErrors table
CREATE INDEX IX_RewardErrors_Timestamp 
    ON "RewardErrors" ("Timestamp" DESC);

CREATE INDEX IX_RewardErrors_IsRetryable_IsResolved 
    ON "RewardErrors" ("IsRetryable", "IsResolved");
```

### Database Maintenance

#### Daily Vacuum (Automated)

```bash
# Add to cron (daily at 2 AM)
0 2 * * * docker exec generated_postgres_1 psql -U postgres -d btcpayservermainnet -c "VACUUM ANALYZE \"BitcoinRewards\"; VACUUM ANALYZE \"RewardErrors\";"
```

#### Archive Old Records

```sql
-- Archive rewards older than 90 days
BEGIN;

-- Create archive table if not exists
CREATE TABLE IF NOT EXISTS "BitcoinRewards_Archive" (LIKE "BitcoinRewards" INCLUDING ALL);

-- Move old records
INSERT INTO "BitcoinRewards_Archive" 
SELECT * FROM "BitcoinRewards" 
WHERE "CreatedAt" < NOW() - INTERVAL '90 days';

-- Delete from main table
DELETE FROM "BitcoinRewards" 
WHERE "CreatedAt" < NOW() - INTERVAL '90 days';

COMMIT;
```

#### Check Database Size

```sql
SELECT 
    schemaname,
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) AS size
FROM pg_tables
WHERE tablename LIKE '%Reward%'
ORDER BY pg_total_relation_size(schemaname||'.'||tablename) DESC;
```

---

## Monitoring & Observability

### Prometheus Integration

#### 1. Configure Prometheus

**prometheus.yml:**
```yaml
scrape_configs:
  - job_name: 'btcpay-bitcoin-rewards'
    static_configs:
      - targets: ['your-btcpay-domain.com:443']
    metrics_path: /api/v1/bitcoin-rewards/metrics
    scheme: https
    scrape_interval: 30s
    scrape_timeout: 10s
```

#### 2. Restart Prometheus

```bash
docker restart prometheus
```

#### 3. Verify Metrics

```bash
# Check Prometheus targets
curl http://localhost:9090/api/v1/targets | jq '.data.activeTargets[] | select(.labels.job=="btcpay-bitcoin-rewards")'
```

### Grafana Dashboard

#### Import Dashboard

1. Grafana → Dashboards → Import
2. Upload `grafana-dashboard.json` (from `docs/` folder)
3. Select Prometheus data source
4. Click Import

**Key Panels:**
- Rewards created (per minute)
- Reward claim rate
- Error rate by type
- Webhook processing latency (p50/p95/p99)
- Active rewards gauge
- Lightning node balance

### Health Checks

#### BTCPay Health Endpoint

```bash
curl https://your-domain.com/health | jq '.entries.bitcoin-rewards'
```

**Expected output:**
```json
{
  "status": "Healthy",
  "description": "Bitcoin Rewards plugin is functioning normally",
  "data": {
    "dbConnected": true,
    "rewardSuccessRate": 0.95,
    "stuckRewards": 0
  }
}
```

#### Plugin-Specific Health

```bash
curl https://your-domain.com/api/v1/bitcoin-rewards/metrics/health
```

### Alerting Rules

**Prometheus alert rules:**

```yaml
groups:
  - name: bitcoin_rewards
    interval: 1m
    rules:
      - alert: HighErrorRate
        expr: rate(errors_total[5m]) > 0.1
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "High error rate in Bitcoin Rewards"
          description: "Error rate is {{ $value | humanize }} errors/sec"

      - alert: WebhookLatencyHigh
        expr: histogram_quantile(0.95, rate(webhook_duration_ms_bucket[5m])) > 2000
        for: 10m
        labels:
          severity: warning
        annotations:
          summary: "High webhook processing latency"
          description: "P95 latency is {{ $value | humanize }}ms"

      - alert: LightningNodeOffline
        expr: up{job="bitcoin-rewards-lightning"} == 0
        for: 2m
        labels:
          severity: critical
        annotations:
          summary: "Lightning node is offline"
          description: "Bitcoin Rewards cannot process payments"

      - alert: StuckRewards
        expr: active_rewards{store=~".*"} > 50
        for: 1h
        labels:
          severity: warning
        annotations:
          summary: "Many unclaimed rewards"
          description: "{{ $value }} rewards stuck for over 1 hour"
```

### Log Aggregation

#### Structured Logging

Plugin uses structured logging with correlation IDs:

```json
{
  "timestamp": "2026-03-28T21:00:00Z",
  "level": "Information",
  "correlationId": "abc123xyz",
  "plugin": "BitcoinRewards",
  "storeId": "store123",
  "rewardId": "reward456",
  "message": "Reward created successfully"
}
```

#### Log Collection

**Using Docker logs:**
```bash
docker logs -f generated_btcpayserver_1 | grep BitcoinRewards
```

**Using journald:**
```bash
journalctl -u btcpayserver -f | grep BitcoinRewards
```

**Send to ELK Stack:**
```yaml
# docker-compose.override.yml
services:
  btcpayserver:
    logging:
      driver: "fluentd"
      options:
        fluentd-address: "localhost:24224"
        tag: "btcpay.bitcoinrewards"
```

---

## Performance Tuning

### Caching Configuration

#### Memory Cache Limits

```csharp
// In BitcoinRewardsPlugin.cs
services.AddMemoryCache(options =>
{
    options.SizeLimit = 1000; // Max 1000 entries
    options.CompactionPercentage = 0.25; // Remove 25% when full
    options.ExpirationScanFrequency = TimeSpan.FromMinutes(5);
});
```

**Environment override:**
```bash
BTCPAY_BITCOINREWARDS_CACHE_SIZE=2000
```

#### Cache Hit Rates

Monitor cache effectiveness:
```bash
# Via metrics endpoint
curl https://your-domain.com/api/v1/bitcoin-rewards/metrics | grep cache_hit_ratio
```

**Target hit rates:**
- Store settings: >95%
- Payout processors: >90%
- Exchange rates: >60%

### Database Connection Pooling

**PostgreSQL configuration:**
```
ConnectionStrings__DefaultConnection=Host=postgres;Database=btcpayservermainnet;Username=postgres;Password=xxx;MinPoolSize=5;MaxPoolSize=100;ConnectionIdleLifetime=300
```

**Optimal settings:**
- `MinPoolSize=5` - Keep 5 connections warm
- `MaxPoolSize=100` - Allow up to 100 concurrent
- `ConnectionIdleLifetime=300` - Close idle after 5 min

### Rate Limiting Tuning

**For high-volume stores:**

```bash
# Increase webhook limit
Webhook Requests Per Minute: 120  # Default: 60
Webhook Burst Size: 20             # Default: 10

# Increase API limit
API Requests Per Minute: 240       # Default: 120
```

### Query Optimization

**Slow query log:**
```sql
-- Enable in postgresql.conf
log_min_duration_statement = 1000  # Log queries > 1s

-- Check slow queries
SELECT query, calls, total_time, mean_time 
FROM pg_stat_statements 
WHERE query LIKE '%BitcoinRewards%'
ORDER BY mean_time DESC 
LIMIT 10;
```

---

## Security Hardening

### Webhook Signature Validation

**Always validate webhook signatures!**

```csharp
// Automatic in plugin - verify config
var signature = Request.Headers["X-Square-Signature"];
var computed = ComputeHMAC(webhookUrl + body, signatureKey);

if (!CryptographicOperations.FixedTimeEquals(signature, computed))
{
    return Unauthorized();
}
```

### Rate Limiting

**Production settings:**
```
Enabled: true
Webhook: 60 req/min
API: 120 req/min
Blacklist: Add known bad actors
```

### IP Whitelist (Optional)

For webhooks from known sources:
```
Whitelist:
- 35.186.255.0/24  # Square webhook IPs
- Your internal network
```

### SSL/TLS

**Required:** All webhook endpoints must use HTTPS.

```nginx
# Nginx config
server {
    listen 443 ssl http2;
    ssl_certificate /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    ...
}
```

### Firewall Rules

```bash
# UFW firewall (Ubuntu)
sudo ufw allow 443/tcp   # HTTPS
sudo ufw allow 9735/tcp  # Lightning (if exposed)
sudo ufw enable
```

---

## Backup & Recovery

### Database Backup

**Automated daily backup:**
```bash
#!/bin/bash
# /etc/cron.daily/backup-btcpay-rewards

BACKUP_DIR=/var/backups/btcpay-rewards
DATE=$(date +%Y%m%d_%H%M%S)

mkdir -p $BACKUP_DIR

# Backup rewards and error tables
docker exec generated_postgres_1 pg_dump -U postgres -d btcpayservermainnet \
    -t BitcoinRewards -t RewardErrors \
    | gzip > $BACKUP_DIR/rewards_$DATE.sql.gz

# Keep last 30 days
find $BACKUP_DIR -name "rewards_*.sql.gz" -mtime +30 -delete
```

### Configuration Backup

```bash
# Backup store settings (JSON)
docker exec generated_postgres_1 psql -U postgres -d btcpayservermainnet \
    -c "COPY (SELECT * FROM \"StoreSettings\" WHERE \"Name\" LIKE '%BitcoinRewards%') TO STDOUT WITH CSV HEADER" \
    > /var/backups/bitcoin-rewards-settings-$(date +%Y%m%d).csv
```

### Disaster Recovery

**Steps to restore:**

1. **Restore database:**
```bash
gunzip < /var/backups/btcpay-rewards/rewards_20260328.sql.gz | \
    docker exec -i generated_postgres_1 psql -U postgres -d btcpayservermainnet
```

2. **Restore plugin files:**
```bash
tar -xzf /var/backups/bitcoin-rewards-plugin.tar.gz \
    -C /var/lib/docker/volumes/generated_btcpay_datadir/_data/Plugins/
```

3. **Restart services:**
```bash
docker restart generated_btcpayserver_1
```

4. **Verify:**
```bash
curl https://your-domain.com/health | jq '.entries.bitcoin-rewards'
```

---

## Troubleshooting

### Common Issues

#### Plugin Not Loading

**Symptoms:** No "Bitcoin Rewards" menu item in store

**Diagnosis:**
```bash
docker logs generated_btcpayserver_1 | grep -i error | grep -i bitcoin
```

**Solutions:**
1. Check plugin files exist: `ls /path/to/plugins/BTCPayServer.Plugins.BitcoinRewards/`
2. Verify permissions: `chown -R btcpay:btcpay ...`
3. Check BTCPay version: Must be 2.3.0+
4. Restart BTCPay: `docker restart generated_btcpayserver_1`

#### Database Migration Failed

**Symptoms:** Error on startup about migrations

**Diagnosis:**
```sql
SELECT * FROM "__EFMigrationsHistory" 
WHERE "MigrationId" LIKE '%BitcoinRewards%';
```

**Solution:**
```bash
# Manual migration (if needed)
docker exec generated_btcpayserver_1 \
    dotnet ef database update --context BitcoinRewardsPluginDbContext
```

#### High Memory Usage

**Symptoms:** BTCPay container using excessive RAM

**Diagnosis:**
```bash
docker stats generated_btcpayserver_1
```

**Solutions:**
1. Reduce cache size: `BTCPAY_BITCOINREWARDS_CACHE_SIZE=500`
2. Clear old errors: Delete resolved errors >30 days
3. Archive old rewards: Move to archive table

#### Webhook Signature Failures

**Symptoms:** All webhooks return 401 Unauthorized

**Diagnosis:**
```bash
docker logs generated_btcpayserver_1 | grep "signature verification failed"
```

**Solutions:**
1. Verify webhook signature key in settings
2. Check webhook URL matches exactly (http vs https, trailing slash)
3. Test with Square's webhook testing tool
4. Enable debug logging temporarily

#### Lightning Node Timeout

**Symptoms:** "Lightning node unavailable" errors

**Diagnosis:**
```bash
# Check LND
docker exec lnd lncli getinfo

# Check connectivity
curl -k https://localhost:8080/v1/getinfo
```

**Solutions:**
1. Restart Lightning node
2. Check channel liquidity
3. Verify RPC connectivity
4. Auto-recovery will retry failed rewards

---

## Maintenance Tasks

### Weekly

- [ ] Review error dashboard
- [ ] Check metrics for anomalies
- [ ] Verify backup completion
- [ ] Monitor disk usage

### Monthly

- [ ] Archive old rewards (>90 days)
- [ ] Vacuum database tables
- [ ] Review and rotate logs
- [ ] Update plugin to latest version

### Quarterly

- [ ] Security audit (dependency scan)
- [ ] Performance review (query optimization)
- [ ] Capacity planning (storage, Lightning channels)
- [ ] Documentation updates

---

## Upgrade Procedure

### Minor/Patch Updates

```bash
# 1. Download latest version
wget https://github.com/jpgaviria2/bitcoinrewards/releases/latest/download/BTCPayServer.Plugins.BitcoinRewards.zip

# 2. Backup current version
tar -czf /var/backups/bitcoin-rewards-$(date +%Y%m%d).tar.gz \
    /var/lib/docker/volumes/generated_btcpay_datadir/_data/Plugins/BTCPayServer.Plugins.BitcoinRewards/

# 3. Extract new version
unzip -o BTCPayServer.Plugins.BitcoinRewards.zip \
    -d /var/lib/docker/volumes/generated_btcpay_datadir/_data/Plugins/BTCPayServer.Plugins.BitcoinRewards/

# 4. Restart
docker restart generated_btcpayserver_1

# 5. Verify
curl https://your-domain.com/health | jq '.entries.bitcoin-rewards'
```

### Major Updates

Follow release notes for breaking changes. May require:
- Database schema migrations
- Configuration updates
- Testing before production deployment

---

## Support Escalation

### Level 1: Self-Service
- Review error dashboard
- Check documentation
- Search GitHub issues

### Level 2: Community Support
- GitHub Discussions
- BTCPay Community Chat
- Community forums

### Level 3: Professional Support
- Email: support@example.com
- Response time: 24-48 hours
- Available for production deployments

---

**Last Updated:** 2026-03-28  
**Version:** 1.4.1  
**Compatibility:** BTCPay Server 2.3.0+
