# NIP-05 Identity System - Deployment Notes

**Deployed:** 2026-03-21  
**Server:** btcpay.anmore.me  
**Domain:** trailscoffee.com  

## Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/plugins/bitcoin-rewards/nip05/nostr.json` | GET | NIP-05 identity lookup (standard) |
| `/plugins/bitcoin-rewards/nip05/check?name=X` | GET | Check username availability |
| `/plugins/bitcoin-rewards/nip05/lookup?pubkey=X` | GET | Reverse lookup by pubkey |
| `/plugins/bitcoin-rewards/wallet/create` | POST | Create wallet (optional `username` field for NIP-05) |
| `/plugins/bitcoin-rewards/nip05/admin/*` | Various | Admin endpoints (requires API key) |

## Pre-Seeded Users (8)

| Username | Pubkey (first 16 chars) |
|----------|------------------------|
| manager | 4123fb4c449d8a48 |
| jp | 88ee46231382525f |
| birchy | e0a59f043d07866 |
| trails | c2c2cda6f2dbc736 |
| pac | 17c122ebefc64979 |
| coffeelover635280 | f4c9457d2a710aec |
| torca | f3f3a288b9551dee |
| coffeelover339076 | 3176ffec038ffb0e |

## Admin API Key Setup

Generated key: `da0f2f4277c78b215f9673d8ed22a7c4eef9e020bf0efa1160445f6ca5d5cf72`

To configure, add to BTCPay environment:
```bash
# Option 1: Docker compose env
# Add to docker-compose.yml under btcpayserver environment:
#   BTCPAY_BITCOINREWARDS_ADMINKEY: da0f2f4277c78b215f9673d8ed22a7c4eef9e020bf0efa1160445f6ca5d5cf72

# Option 2: Docker run env
sudo docker exec btcpay-dev bash -c 'export BTCPAY_BITCOINREWARDS_ADMINKEY=da0f2f4277c78b215f9673d8ed22a7c4eef9e020bf0efa1160445f6ca5d5cf72'
```

Use in requests: `Authorization: Bearer <key>`

## Verify Deployment

```bash
# Check plugin loaded
curl -s https://btcpay.anmore.me/plugins/bitcoin-rewards/nip05/nostr.json | jq '.names | length'
# Expected: 8+

# Check migration
sudo docker logs btcpay-dev --since=5m 2>&1 | grep "Bitcoin Rewards plugin migrations"
```

## Backup

Script: `/home/ln/backup-nip05.sh`

Setup cron:
```bash
sudo crontab -e
# Add: 0 3 * * * /home/ln/backup-nip05.sh >> /var/log/nip05-backup.log 2>&1
```

## Rate Limiting

- Username checks: built-in cooldown per IP
- Wallet creation: standard BTCPay rate limits apply
- nostr.json: cached, lightweight query

## Troubleshooting

| Issue | Solution |
|-------|----------|
| nostr.json returns empty | Check DB migration: `sudo docker logs btcpay-dev \| grep migration` |
| 404 on endpoints | Plugin not loaded; check `/root/.btcpayserver/Plugins/` in container |
| Username "reserved" | Pre-seeded names are taken; use different username |
| Offensive word rejected | Built-in filter; user gets auto-suggested alternative |
| Migration fails | Check PostgreSQL connectivity; review full logs |

## Redeployment

```bash
cd /home/ln/.openclaw/workspace/btcpay-research/bitcoinrewards
sudo docker exec btcpay-dev rm -rf /root/.btcpayserver/Plugins/BTCPayServer.Plugins.BitcoinRewards/
sudo docker cp Plugins/BTCPayServer.Plugins.BitcoinRewards/bin/Release/net8.0/. \
  btcpay-dev:/root/.btcpayserver/Plugins/BTCPayServer.Plugins.BitcoinRewards/
sudo docker restart btcpay-dev
```
