# Bitcoin Rewards Plugin - Deployment Guide

**Version:** 2.0 (Production Hardening)  
**Target:** BTCPay Server 2.3.0+  
**Platform:** Docker or bare-metal .NET 8.0

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Build Process](#build-process)
3. [Installation](#installation)
4. [Configuration](#configuration)
5. [Deployment Checklist](#deployment-checklist)
6. [Verification](#verification)
7. [Rollback](#rollback)
8. [Troubleshooting](#troubleshooting)

---

## Prerequisites

### System Requirements

- **BTCPay Server:** Version 2.3.0 or higher
- **Runtime:** .NET 8.0
- **Database:** PostgreSQL (via BTCPay)
- **Lightning:** LND, CLN, or Eclair configured in BTCPay
- **Memory:** 512MB+ available
- **Disk:** 100MB+ for plugin files

### Developer Requirements (Build from Source)

- **.NET SDK:** 8.0.418 or higher
- **Docker:** For containerized builds (optional)
- **Git:** For cloning repository

---

## Build Process

### Option A: Docker Build (Recommended)

```bash
# Clone repository
git clone https://github.com/jpgaviria2/bitcoinrewards.git
cd bitcoinrewards

# Build using Docker
sudo docker run --rm \
  -v $(pwd):/build \
  -w /build/Plugins/BTCPayServer.Plugins.BitcoinRewards \
  btcpayserver-plugin-bitcoinrewards-build \
  dotnet build BTCPayServer.Plugins.BitcoinRewards.csproj -c Release

# Check build output
ls -lh Plugins/BTCPayServer.Plugins.BitcoinRewards/bin/Release/net8.0/
```

### Option B: Manual Build

```bash
# Clone repository
git clone https://github.com/jpgaviria2/bitcoinrewards.git
cd bitcoinrewards

# Build plugin
cd Plugins/BTCPayServer.Plugins.BitcoinRewards
dotnet build -c Release

# Build output location
cd bin/Release/net8.0/
```

**Build Artifacts:**
- `BTCPayServer.Plugins.BitcoinRewards.dll`
- `BTCPayServer.Plugins.BitcoinRewards.pdb`
- `BTCPayServer.Plugins.BitcoinRewards.Views.dll` (if any)
- Dependencies (third-party NuGet packages)

---

## Installation

### Docker BTCPay Server (Standard Deployment)

```bash
# 1. Stop BTCPay container
sudo docker stop btcpay-server

# 2. Remove old plugin files (if upgrading)
sudo docker exec btcpay-server rm -rf /root/.btcpayserver/Plugins/BTCPayServer.Plugins.BitcoinRewards/

# 3. Copy new plugin files
sudo docker cp \
  ./Plugins/BTCPayServer.Plugins.BitcoinRewards/bin/Release/net8.0/. \
  btcpay-server:/root/.btcpayserver/Plugins/BTCPayServer.Plugins.BitcoinRewards/

# 4. Restart BTCPay
sudo docker restart btcpay-server

# 5. Wait for startup (30-60 seconds)
sudo docker logs -f btcpay-server | grep -i "bitcoinrewards\|plugin"
```

### Bare-Metal BTCPay Server

```bash
# 1. Stop BTCPay service
sudo systemctl stop btcpayserver

# 2. Copy plugin files
cp -r ./Plugins/BTCPayServer.Plugins.BitcoinRewards/bin/Release/net8.0/* \
  /var/lib/btcpayserver/.btcpayserver/Plugins/BTCPayServer.Plugins.BitcoinRewards/

# 3. Set permissions
chown -R btcpayserver:btcpayserver /var/lib/btcpayserver/.btcpayserver/Plugins/

# 4. Start BTCPay
sudo systemctl start btcpayserver

# 5. Check logs
sudo journalctl -u btcpayserver -f | grep -i "bitcoinrewards\|plugin"
```

---

## Configuration

### Environment Variables (Optional)

Set in docker-compose.yml or systemd service file:

```bash
# Exchange rate update interval (default: 5 minutes)
BTCPAY_BITCOINREWARDS_RATE_INTERVAL=300

# Idempotency cache retention (default: 24 hours)
BTCPAY_BITCOINREWARDS_IDEMPOTENCY_TTL=86400

# Maintenance cleanup interval (default: 1 hour)
BTCPAY_BITCOINREWARDS_MAINTENANCE_INTERVAL=3600
```

### Store-Level Configuration

1. **Login to BTCPay Server** as store owner
2. **Navigate to:** Store Settings → Plugins → Bitcoin Rewards
3. **Configure:**
   - Lightning Node (must be configured first in BTCPay)
   - Default CAD exchange rate source
   - Auto-convert settings
   - Reward percentages

### Database Migrations

**Automatic:** Plugin runs migrations on first startup.

**Manual Check:**
```sql
-- Connect to BTCPay PostgreSQL database
psql -U btcpayserver btcpayserver

-- Check if tables exist
\dt *bitcoinrewards*

-- Expected tables:
-- - bitcoin_rewards_plugin_customerwallets
-- - bitcoin_rewards_plugin_wallettransactions
-- - bitcoin_rewards_plugin_pendinglnurlclaims
-- - bitcoin_rewards_plugin_boltcardlinks
```

---

## Deployment Checklist

### Pre-Deployment

- [ ] **Backup database:** `pg_dump btcpayserver > btcpay_backup_$(date +%F).sql`
- [ ] **Build succeeds:** No compilation errors
- [ ] **Tests pass:** Run `dotnet test` (if tests configured)
- [ ] **Tag release:** `git tag v2.0.0 && git push origin v2.0.0`
- [ ] **Document changes:** Update CHANGELOG.md

### Deployment

- [ ] **Stop BTCPay server**
- [ ] **Remove old plugin files**
- [ ] **Copy new plugin files**
- [ ] **Verify file ownership/permissions**
- [ ] **Start BTCPay server**
- [ ] **Monitor logs** for errors

### Post-Deployment

- [ ] **Plugin loaded:** Check BTCPay logs for "BitcoinRewards plugin loaded"
- [ ] **Database migrations:** Check for migration success messages
- [ ] **API endpoints:** Test `/plugins/bitcoin-rewards/wallet/create`
- [ ] **Lightning connectivity:** Verify Lightning node accessible
- [ ] **Exchange rates:** Check CAD/BTC rate updating
- [ ] **Monitor errors:** Watch logs for 15 minutes

---

## Verification

### 1. Plugin Loaded Check

```bash
# Docker
sudo docker logs btcpay-server 2>&1 | grep -i "bitcoinrewards\|plugin" | tail -20

# Expected output:
# [INFO] BitcoinRewards plugin loaded
# [INFO] Database migrations applied successfully
# [INFO] MaintenanceService started
# [INFO] LnurlClaimWatcherService started
```

### 2. API Health Check

```bash
# Create test wallet
curl -X POST https://your-btcpay.com/plugins/bitcoin-rewards/wallet/create \
  -H "Content-Type: application/json" \
  -d '{"storeId":"YOUR_STORE_ID","autoConvertToCad":true}'

# Expected: 200 OK with wallet ID and token
```

### 3. Database Check

```sql
-- Check wallet count
SELECT COUNT(*) FROM bitcoin_rewards_plugin_customerwallets;

-- Check recent transactions
SELECT * FROM bitcoin_rewards_plugin_wallettransactions 
ORDER BY "CreatedAt" DESC LIMIT 10;
```

### 4. Service Status

```bash
# Check hosted services running
sudo docker logs btcpay-server 2>&1 | grep "HostedService\|BackgroundService"

# Expected:
# - MaintenanceService running
# - LnurlClaimWatcherService running
# - BtcpayInvoiceRewardHostedService running
```

---

## Rollback

### Quick Rollback (Docker)

```bash
# 1. Stop current version
sudo docker stop btcpay-server

# 2. Restore previous plugin files from backup
sudo docker cp backup/BitcoinRewards/. \
  btcpay-server:/root/.btcpayserver/Plugins/BTCPayServer.Plugins.BitcoinRewards/

# 3. Restart
sudo docker restart btcpay-server
```

### Database Rollback

```bash
# Only if migrations caused issues

# 1. Restore database backup
psql -U btcpayserver btcpayserver < btcpay_backup_2026-03-20.sql

# 2. Rollback plugin files (see above)

# 3. Restart BTCPay
```

### Version Pinning

```bash
# To prevent auto-updates, use specific git tag
git checkout v1.2.0

# Build and deploy that version
```

---

## Troubleshooting

### Plugin Not Loading

**Symptoms:** No "BitcoinRewards" in logs, plugin menu missing

**Fixes:**
```bash
# Check plugin directory exists
sudo docker exec btcpay-server ls -la /root/.btcpayserver/Plugins/

# Check file permissions
sudo docker exec btcpay-server ls -la /root/.btcpayserver/Plugins/BTCPayServer.Plugins.BitcoinRewards/

# Check DLL exists
sudo docker exec btcpay-server ls -la /root/.btcpayserver/Plugins/BTCPayServer.Plugins.BitcoinRewards/*.dll

# BTCPay logs for plugin loader errors
sudo docker logs btcpay-server 2>&1 | grep -i "plugin.*error\|failed to load"
```

### Database Migration Errors

**Symptoms:** "Migration failed" in logs

**Fixes:**
```sql
-- Check migration history
SELECT * FROM "__EFMigrationsHistory" 
WHERE "MigrationId" LIKE '%BitcoinRewards%';

-- Manually run migration (if needed)
-- Contact support - don't manually edit schema
```

### Lightning Payment Failures

**Symptoms:** `pay-invoice` returns `PAYMENT_FAILED`

**Checks:**
```bash
# Verify Lightning node connected
# BTCPay Server → Store Settings → Lightning → Test Connection

# Check Lightning logs
sudo docker logs lnd 2>&1 | tail -50

# Check BTCPay Lightning configuration
sudo docker exec btcpay-server cat /datadir/Main/settings.config | grep Lightning
```

### Exchange Rate Not Updating

**Symptoms:** Old CAD/BTC rate, swap failures

**Fixes:**
```bash
# Check ExchangeRateService logs
sudo docker logs btcpay-server 2>&1 | grep ExchangeRateService

# Manually trigger update (requires code access)
# Or restart BTCPay to force refresh
```

### Idempotency Cache Growing

**Symptoms:** High memory usage after days

**Check:**
```bash
# MaintenanceService should clean hourly
sudo docker logs btcpay-server 2>&1 | grep MaintenanceService

# Expected every hour:
# [INFO] Cleaned up N expired idempotency entries

# If not running, check service registration
```

### Rate Limiting Too Strict

**Symptoms:** Legitimate users hitting 429 errors

**Adjust** (requires code change):
```csharp
// Middleware/RateLimitMiddleware.cs
// Increase limits as needed
_endpointLimits["/pay-invoice"] = (50, TimeSpan.FromMinutes(1)); // was 20
```

---

## Production Best Practices

### Monitoring

- **Setup alerts** for error rate > 5%
- **Monitor wallet creation rate** (spike = potential abuse)
- **Track payment success rate** (< 90% = Lightning issues)
- **Database size** (should grow linearly with wallets)

### Backups

- **Database:** Daily automated backups
- **Plugin config:** Store in version control
- **Recovery Time Objective:** < 1 hour

### Security

- **Keep BTCPay updated:** Patch security vulnerabilities
- **Rate limits enforced:** Prevents DOS attacks
- **Token security:** Wallet tokens are cryptographically secure
- **TLS required:** Never serve over HTTP

### Performance

- **Expected load:** 1000 req/min (mixed endpoints)
- **p95 latency:** < 1s (excluding Lightning 60s timeout)
- **Database connections:** Pooled via EF Core
- **Memory:** ~200MB under normal load

---

## Support Contacts

**GitHub Issues:** https://github.com/jpgaviria2/bitcoinrewards/issues  
**Documentation:** See repository README.md  
**Emergency:** Contact BTCPay Server support channel

---

**Deployment Version:** 2.0  
**Last Updated:** 2026-03-20  
**Next Review:** 2026-06-20 (quarterly)
