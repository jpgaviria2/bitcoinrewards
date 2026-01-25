# Troubleshooting Guide

## Common Issues and Solutions

### Rate Fetching Failures

#### Problem: "No BTC rate available for currency CAD/USD/EUR"

**Symptoms:**
- Rewards fail to process
- Logs show: `No BTC rate available for currency XXX`
- No satoshi amount calculated

**Causes:**
1. Store rate rules not configured
2. Rate provider (Kraken, Bylls, etc.) is down
3. Currency not supported by configured provider
4. Network connectivity issues

**Solutions:**

1. **Check Store Rate Configuration:**
   - Go to BTCPay Server → Stores → [Your Store] → Rates
   - Verify primary rate source is configured (e.g., `X_CAD = kraken(X_CAD);`)
   - Add fallback rules if needed

2. **Test Rate Manually:**
   ```bash
   # Check if rate provider is responding
   curl https://api.kraken.com/0/public/Ticker?pair=XBTCAD
   ```

3. **Enable Debug Logging:**
   - In `appsettings.json` or environment variables:
   ```json
   {
     "Logging": {
       "LogLevel": {
         "BTCPayServer.Plugins.BitcoinRewards": "Debug"
       }
     }
   }
   ```
   - Check logs for `[RATE FETCH]` entries with detailed diagnostics

4. **Verify Currency Support:**
   - Kraken supports: USD, EUR, CAD, JPY, GBP, CHF, AUD
   - For unsupported currencies, use a currency pair like:
     ```
     X_MXN = coingecko(X_MXN);
     ```

---

### Email Notifications Not Sending

#### Problem: Rewards created but customers don't receive emails

**Symptoms:**
- Rewards appear in database
- Pull payment links generated
- No emails delivered
- Logs show: "Email service not available"

**Causes:**
1. Email plugin not installed/configured
2. SMTP settings incorrect
3. Email template rendering errors
4. Recipient email invalid

**Solutions:**

1. **Verify Email Plugin:**
   - Go to Server Settings → Plugins
   - Ensure email plugin is installed and enabled
   - Test email configuration: Server Settings → Emails → Send Test Email

2. **Check SMTP Settings:**
   - Verify SMTP server, port, credentials
   - For Gmail: Enable "App Passwords" if using 2FA
   - For Office365: Use app-specific password

3. **Review Email Template:**
   - Go to Stores → [Store] → Plugins → Bitcoin Rewards → Settings
   - Check email template for syntax errors
   - Available tokens: `{RewardAmountBTC}`, `{RewardAmountSatoshis}`, `{ClaimLink}`, `{OrderId}`, `{StoreId}`

4. **Check Logs:**
   ```bash
   docker logs btcpayserver | grep -i "email"
   ```

---

### Square Webhook Signature Verification Fails

#### Problem: Square webhooks return 401 Unauthorized

**Symptoms:**
- Square payments complete but no rewards created
- Logs show: "Square webhook signature verification failed"
- Webhook shows "Unauthorized" in Square dashboard

**Causes:**
1. Incorrect signature key configured
2. Webhook URL mismatch
3. HTTPS/HTTP protocol mismatch
4. Reverse proxy header issues

**Solutions:**

1. **Verify Signature Key:**
   - In Square Developer Dashboard, copy exact signature key
   - In BTCPay: Stores → [Store] → Plugins → Bitcoin Rewards → Settings
   - Paste key exactly (no extra spaces)

2. **Check Webhook URL:**
   - Webhook URL must exactly match what Square sends to
   - Format: `https://your-domain.com/plugins/bitcoin-rewards/{storeId}/webhooks/square`
   - **Important:** Must use HTTPS in production
   - Test with: `https://your-btcpay-domain.com` (no trailing slash)

3. **Configure Square Webhook:**
   - Square Dashboard → Webhooks
   - Add new webhook endpoint
   - Subscribe to: `payment.updated`
   - Enter URL: `https://your-domain/plugins/bitcoin-rewards/YOUR_STORE_ID/webhooks/square`
   - Save signature key

4. **Check Reverse Proxy Headers:**
   - If behind nginx/caddy, ensure headers forwarded:
   ```nginx
   proxy_set_header Host $host;
   proxy_set_header X-Real-IP $remote_addr;
   proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
   proxy_set_header X-Forwarded-Proto $scheme;
   ```

---

### Pull Payment Claims Fail

#### Problem: Customers can't claim rewards via LNURL

**Symptoms:**
- QR code displays correctly
- Wallet scans but claim fails
- Error: "Pull payment not found" or "Already claimed"

**Causes:**
1. Pull payment expired
2. Lightning node offline
3. Insufficient inbound liquidity
4. Pull payment already claimed

**Solutions:**

1. **Check Pull Payment Status:**
   - BTCPay → Pull Payments → Find by ID
   - Verify status is "Active" not "Expired" or "Completed"

2. **Verify Lightning Node:**
   - Ensure Lightning node is connected and synced
   - Check inbound liquidity: `lncli channelbalance`

3. **Test Lightning Connection:**
   - Try claiming small amount first (1 sat)
   - Check BTCPay logs for Lightning errors

4. **Adjust Expiry Settings:**
   - Stores → [Store] → Plugins → Bitcoin Rewards → Settings
   - Increase "Pull Payment Expiry" from default 7 days

---

### High Memory Usage / Performance

#### Problem: Plugin consuming excessive resources

**Symptoms:**
- BTCPay Server slow to respond
- High memory usage
- Rate fetch timeouts

**Causes:**
1. Too many unclaimed rewards in database
2. Rate fetch caching disabled
3. Excessive logging enabled

**Solutions:**

1. **Enable Database Cleanup:**
   - Stores → [Store] → Plugins → Bitcoin Rewards → Settings
   - Enable "Auto-cleanup Old Rewards"
   - Set retention: 30 days for claimed, 90 days for unclaimed

2. **Check Database Size:**
   ```sql
   SELECT COUNT(*) FROM btcpayrewards.bitcoin_rewards;
   SELECT COUNT(*) FROM btcpayrewards.bitcoin_rewards WHERE status = 'Claimed';
   ```

3. **Reduce Logging Level:**
   - Disable Debug logging in production
   - Set to Warning or Error level:
   ```json
   "BTCPayServer.Plugins.BitcoinRewards": "Warning"
   ```

4. **Manual Cleanup:**
   ```sql
   DELETE FROM btcpayrewards.bitcoin_rewards 
   WHERE status = 'Claimed' AND created_at < NOW() - INTERVAL '90 days';
   ```

---

### Reward Amounts Incorrect

#### Problem: Calculated reward amount doesn't match expected

**Symptoms:**
- Reward percentage seems wrong
- Satoshi amounts too high/low
- Different from test calculations

**Causes:**
1. Rate timestamp mismatch
2. Percentage applied to wrong amount
3. Currency conversion errors
4. Minimum satoshi enforcement

**Solutions:**

1. **Verify Reward Percentage:**
   - Check: Stores → [Store] → Plugins → Bitcoin Rewards → Settings
   - Separate percentages for External (Square) vs BTCPay invoices
   - Values are 0-100, not decimals (5 = 5%, not 0.05%)

2. **Understand Calculation:**
   ```
   Reward Amount (fiat) = Transaction Amount * (Reward % / 100)
   Reward Amount (BTC) = Reward Amount (fiat) / BTC Rate
   Reward Amount (sats) = Reward Amount (BTC) * 100,000,000
   ```

3. **Check Minimum Enforcement:**
   - Plugin enforces minimum 1 satoshi
   - Amounts < 1 sat are rounded up to 1 sat

4. **Test with Debug Logs:**
   - Enable debug logging
   - Check `[RATE FETCH]` logs for exact rate used
   - Verify transaction amount in logs

---

## Getting Help

### Before Opening an Issue

1. Check this troubleshooting guide
2. Enable Debug logging and collect logs
3. Test with a small amount first
4. Check BTCPay Server version compatibility (>= 2.3.0)

### Opening an Issue

Include:
- BTCPay Server version
- Plugin version
- Relevant log excerpts (with sensitive data removed)
- Steps to reproduce
- Expected vs actual behavior

**GitHub Issues:** https://github.com/jpgaviria2/bitcoinrewards/issues

### Community Support

- BTCPay Server Community: https://chat.btcpayserver.org
- BTCPay Documentation: https://docs.btcpayserver.org

---

## Debug Commands

### Check Plugin Version
```bash
docker exec btcpayserver ls -la /root/.btcpayserver/Plugins/BTCPayServer.Plugins.BitcoinRewards/
```

### View Recent Logs
```bash
docker logs btcpayserver --tail 200 | grep "BitcoinRewards"
```

### Test Rate Fetch
```bash
# From BTCPay container
curl http://localhost:5000/api/v1/stores/{storeId}/rates/BTC_USD
```

### Database Queries
```sql
-- Recent rewards
SELECT * FROM btcpayrewards.bitcoin_rewards 
ORDER BY created_at DESC LIMIT 10;

-- Unclaimed rewards count
SELECT COUNT(*) FROM btcpayrewards.bitcoin_rewards 
WHERE status != 'Claimed';

-- Rewards by platform
SELECT platform, COUNT(*), SUM(amount_satoshis) 
FROM btcpayrewards.bitcoin_rewards 
GROUP BY platform;
```
