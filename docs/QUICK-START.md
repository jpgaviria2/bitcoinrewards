# Bitcoin Rewards - Quick Start Guide

Get your Bitcoin rewards system running in 15 minutes!

---

## Prerequisites Checklist

- [ ] BTCPay Server v2.3.0+ installed
- [ ] Lightning node configured (LND or Core Lightning)
- [ ] Square account (for payment integration)
- [ ] Email configured in BTCPay (optional but recommended)

---

## Step 1: Install Plugin (2 minutes)

**Option A: Via BTCPay UI**
1. Settings → Plugins → Available Plugins
2. Find "Bitcoin Rewards" → Click Install
3. Restart BTCPay Server

**Option B: Manual Install**
```bash
cd /var/lib/docker/volumes/generated_btcpay_datadir/_data/Plugins
wget https://github.com/jpgaviria2/bitcoinrewards/releases/latest/download/BTCPayServer.Plugins.BitcoinRewards.zip
unzip BTCPayServer.Plugins.BitcoinRewards.zip -d BTCPayServer.Plugins.BitcoinRewards/
docker restart generated_btcpayserver_1
```

✅ **Verify:** You should see "Bitcoin Rewards" in your store sidebar

---

## Step 2: Enable & Configure (3 minutes)

1. Navigate to: **Store → Bitcoin Rewards → Settings**

2. **Enable plugin:**
   ```
   ☑ Enable Bitcoin Rewards
   ```

3. **Set reward percentage:**
   ```
   External Reward Percentage: 5%
   BTCPay Reward Percentage: 10%
   ```

4. **Set minimum transaction:**
   ```
   Minimum Transaction Amount: $5.00
   ```

5. **Set maximum reward (security):**
   ```
   Maximum Single Reward: 100,000 sats
   ```

6. Click **Save**

✅ **Verify:** Settings saved successfully message

---

## Step 3: Square Integration (5 minutes)

### Get Square Credentials

1. Go to [Square Developer Dashboard](https://developer.squareup.com/)
2. Create/select application
3. Copy these values:
   - **Application ID:** `sq0idp-xxxxx`
   - **Access Token:** `EAAA...`
   - **Webhook Signature Key:** (from Webhooks tab)

### Configure in BTCPay

1. **Store → Bitcoin Rewards → Settings**
2. Scroll to **Square Integration**
3. Paste credentials:
   ```
   Application ID: [paste here]
   Access Token: [paste here]
   Webhook Signature Key: [paste here]
   ```
4. Click **Save**

### Register Webhook URL

1. Back in Square Dashboard → Webhooks
2. Click **Add Endpoint**
3. Enter URL:
   ```
   https://your-btcpay-domain.com/plugins/bitcoin-rewards/YOUR_STORE_ID/webhooks/square
   ```
   
   **Find YOUR_STORE_ID:** Store → Settings → Store ID (e.g., `DWJ4gyqw...`)

4. Subscribe to: **`payment.updated`**
5. Click **Save**

✅ **Verify:** Webhook shows "Active" in Square dashboard

---

## Step 4: Test It (3 minutes)

### Test Purchase

1. Make a test payment at your Square terminal
2. Amount: At least $5.00 (your minimum)

### Check Reward Created

1. **Store → Bitcoin Rewards → Errors**
2. If no errors → Success! 🎉
3. If errors → See troubleshooting below

### Check Email Sent

1. Check the email address from test purchase
2. Look for subject: "Your Bitcoin Reward from {Store}"
3. Click claim link to test

✅ **Success:** Customer receives email with Bitcoin claim link

---

## Step 5: Customer Display (2 minutes - Optional)

### Display URL

Get your display URL:
```
https://your-btcpay-domain.com/plugins/bitcoin-rewards/YOUR_STORE_ID/display
```

### Set Up Tablet

1. Open browser on Android tablet/iPad
2. Navigate to display URL
3. Enable full screen (F11 or kiosk mode)
4. Place near register

✅ **Result:** Customers see their Bitcoin rewards on screen!

---

## Customization (Optional)

### Branding

1. **Store → Bitcoin Rewards → Settings**
2. Scroll to **Display Template**
3. Set colors:
   ```
   Primary Color: #6B4423 (your brand color)
   Secondary Color: #CD853F
   Logo URL: https://your-domain.com/logo.png
   ```

### Email Template

1. Edit **Email Template** field
2. Use tokens:
   - `{STORE_NAME}` - Your store name
   - `{REWARD_AMOUNT_SATS}` - Reward in satoshis
   - `{CLAIM_LINK}` - The actual claim link

---

## Troubleshooting

### "No Bitcoin Rewards menu item"
❌ Plugin not loaded  
✅ **Fix:** Restart BTCPay: `docker restart generated_btcpayserver_1`

### "401 Unauthorized" in webhook
❌ Signature mismatch  
✅ **Fix:** Double-check webhook signature key (no spaces, exact match)

### "Email not sent"
❌ Email plugin not configured  
✅ **Fix:** Server Settings → Emails → Configure SMTP

### "Reward amount is 0"
❌ Transaction below minimum  
✅ **Fix:** Check minimum transaction amount setting

### "Lightning node offline"
❌ LND/Core Lightning unavailable  
✅ **Fix:** `docker restart lnd` (auto-recovery will retry)

---

## Monitoring Dashboard

Check plugin health:
```
https://your-domain.com/health
```

Look for:
```json
{
  "bitcoin-rewards": {
    "status": "Healthy",
    "data": {
      "dbConnected": true,
      "rewardSuccessRate": 0.95
    }
  }
}
```

---

## Next Steps

### Production Checklist

- [ ] Test with real (small) purchase
- [ ] Configure rate limiting (prevent abuse)
- [ ] Set up Prometheus monitoring
- [ ] Review error dashboard daily (first week)
- [ ] Add backup cron job

### Advanced Features

- **Metrics Dashboard:** Set up Prometheus + Grafana
- **Custom Branding:** Full HTML/CSS customization
- **Multi-Store:** Run on multiple stores
- **Rate Limiting:** Configure per store

---

## Documentation

**Full Guides:**
- [User Guide](./USER-GUIDE.md) - Complete store owner guide
- [Admin Guide](./ADMIN-GUIDE.md) - System administrator guide
- [API Reference](./API-REFERENCE.md) - Developer API docs

**GitHub:**
- Issues: https://github.com/jpgaviria2/bitcoinrewards/issues
- Discussions: https://github.com/jpgaviria2/bitcoinrewards/discussions

---

## Support

**Community:**
- BTCPay Chat: https://chat.btcpayserver.org/
- GitHub Discussions

**Professional:**
- Email: support@example.com (production deployments)

---

**🎉 Congratulations!** You're now rewarding customers with Bitcoin!

**Time to first reward:** ~15 minutes  
**Difficulty:** ⭐⭐ (Intermediate)  
**ROI:** Increased customer loyalty & Lightning adoption

---

**Last Updated:** 2026-03-28  
**Version:** 1.4.1  
**Quick Start Version:** v1.0
