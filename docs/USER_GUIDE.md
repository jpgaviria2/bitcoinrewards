# Bitcoin Rewards Plugin - User Guide

## Table of Contents
1. [Installation](#installation)
2. [Initial Setup](#initial-setup)
3. [Square Integration](#square-integration)
4. [BTCPay Invoice Rewards](#btcpay-invoice-rewards)
5. [Customizing Display Page](#customizing-display-page)
6. [Email Configuration](#email-configuration)
7. [Testing](#testing)
8. [Best Practices](#best-practices)

---

## Installation

### Prerequisites
- BTCPay Server version 2.3.0 or higher
- PostgreSQL database
- (Optional) Email plugin for notifications
- (Optional) Square account for external POS integration

### Install Plugin

#### Method 1: Via BTCPay Server UI (Recommended)
1. Log in to BTCPay Server as admin
2. Go to **Server Settings** → **Plugins**
3. Click **"Upload Plugin"**
4. Upload the `.btcpay` file
5. Click **"Install"** and restart BTCPay Server

#### Method 2: Manual Installation
```bash
# Download latest release
wget https://github.com/jpgaviria2/bitcoinrewards/releases/latest/download/BTCPayServer.Plugins.BitcoinRewards.btcpay

# Copy to plugins directory
sudo cp BTCPayServer.Plugins.BitcoinRewards.btcpay /var/lib/docker/volumes/generated_btcpay_datadir/_data/Plugins/

# Restart BTCPay Server
sudo docker restart btcpayserver
```

### Verify Installation
1. After restart, go to **Server Settings** → **Plugins**
2. You should see "Bitcoin Rewards" version 1.1.0 listed
3. Status should show "Enabled"

---

## Initial Setup

### 1. Configure Store Settings

1. Navigate to **Stores** → [Select Your Store] → **Plugins** → **Bitcoin Rewards**
2. You'll see the settings page with several sections

### 2. Basic Configuration

#### Enable/Disable the Plugin
- Toggle "Enable Bitcoin Rewards" to activate

#### Reward Percentages
- **External Platform Reward %**: Applies to Square transactions (0-100)
- **BTCPay Reward %**: Applies to native BTCPay invoices (0-100)
- Example: `5` means 5% of transaction amount

```
Example: $100 purchase with 5% = $5 reward
If BTC = $50,000, reward = 10,000 satoshis
```

#### Platform Selection
- **Square**: Enable for Square POS integration
- **BTCPay**: Enable for native invoice rewards
- **Both**: Enable both platforms

### 3. Configure Rate Provider

Rate providers fetch current BTC exchange rates:

1. Go to **Stores** → [Your Store] → **Rates**
2. Set primary rules (example for CAD):
   ```
   X_CAD = kraken(X_CAD);
   ```
3. Set fallback rules:
   ```
   X_X = bylls(X_X);
   ```
4. Set spread (optional): `0.01` (adds 0.01% margin)

**Supported Providers:**
- `kraken` - Recommended for USD, EUR, CAD, GBP, JPY, AUD, CHF
- `bylls` - Good fallback option
- `coingecko` - Wide currency support

---

## Square Integration

### Prerequisites
- Active Square account
- Square Developer account access
- HTTPS-enabled BTCPay Server (required for webhooks)

### Step 1: Get Store ID

1. In BTCPay, go to **Stores** → [Your Store] → **Settings**
2. Copy your Store ID (e.g., `DWJ4gyqwVYkSQBgDD7py2DW5izoNnCD9PBbK7P332hW8`)

### Step 2: Configure Square Webhook

1. Log in to [Square Developer Dashboard](https://developer.squareup.com/apps)
2. Select your application
3. Go to **Webhooks** section
4. Click **"Add Endpoint"**
5. Enter webhook URL:
   ```
   https://your-btcpay-domain.com/plugins/bitcoin-rewards/YOUR_STORE_ID/webhooks/square
   ```
   Replace `YOUR_STORE_ID` with your actual Store ID
6. Select event: **`payment.updated`**
7. Click **"Save"**
8. Copy the **Signature Key** (you'll need this next)

### Step 3: Configure Plugin Settings

1. Go to **Stores** → [Your Store] → **Plugins** → **Bitcoin Rewards**
2. Scroll to **Square Settings**
3. Paste the **Signature Key** from Step 2
4. Click **"Save Settings"**

### Step 4: Test Integration

1. Make a test payment through Square (can use Square sandbox)
2. Check BTCPay logs for webhook received:
   ```bash
   docker logs btcpayserver | grep "Square webhook"
   ```
3. Verify reward created in **Rewards History**

---

## BTCPay Invoice Rewards

Rewards can be automatically created for BTCPay Server invoices.

### Enable BTCPay Rewards

1. **Stores** → [Your Store] → **Plugins** → **Bitcoin Rewards** → **Settings**
2. Enable **"BTCPay"** platform
3. Set **BTCPay Reward %** (e.g., `2` for 2%)
4. **Save Settings**

### How It Works

1. Customer creates invoice for a product/service
2. Customer pays invoice (any payment method: Lightning, on-chain, etc.)
3. Plugin automatically:
   - Calculates reward based on invoice amount
   - Creates pull payment
   - Sends email notification (if configured)
   - Generates claim link

### Reward Eligibility

Rewards are created for invoices that:
- ✅ Status = "Settled" (fully paid)
- ✅ Not marked as a reward claim itself
- ✅ Platform rewards enabled in settings
- ✅ Valid email address attached (for notifications)

---

## Customizing Display Page

The display page shows customers their reward with QR code for claiming.

### Display Page URL Format
```
https://your-btcpay-domain.com/plugins/bitcoin-rewards/STORE_ID/display/REWARD_ID
```

### Customize Template

1. **Stores** → [Your Store] → **Plugins** → **Bitcoin Rewards** → **Settings**
2. Scroll to **Display Template**
3. Edit the HTML template
4. Use available tokens:

#### Available Tokens
| Token | Description | Example |
|-------|-------------|---------|
| `{RewardAmountBTC}` | Amount in BTC | 0.00010000 |
| `{RewardAmountSatoshis}` | Amount in satoshis | 10000 |
| `{ClaimLink}` | LNURL claim link | LNURL1... |
| `{OrderId}` | Original order/transaction ID | ORD-123 |
| `{StoreName}` | Your store name | My Coffee Shop |

#### Example Custom Template
```html
<div class="reward-card">
  <h1>🎉 Congratulations!</h1>
  <p>You earned <strong>{RewardAmountSatoshis} sats</strong></p>
  <p>({RewardAmountBTC} BTC)</p>
  
  <div class="qr-code">
    <!-- QR code auto-generated -->
  </div>
  
  <p>Scan with Lightning wallet to claim!</p>
  <small>Order: {OrderId}</small>
</div>

<style>
  .reward-card {
    text-align: center;
    padding: 20px;
    background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
    color: white;
    border-radius: 10px;
  }
</style>
```

---

## Email Configuration

Send automated reward notifications to customers.

### Prerequisites
- BTCPay Server Email plugin installed and configured
- SMTP settings configured in BTCPay

### Enable Email Notifications

1. **Stores** → [Your Store] → **Plugins** → **Bitcoin Rewards** → **Settings**
2. Scroll to **Email Delivery**
3. Toggle **"Enable Email Notifications"**
4. **Save Settings**

### Customize Email Template

#### Available Tokens
- `{RewardAmountBTC}` - Reward in BTC
- `{RewardAmountSatoshis}` - Reward in satoshis
- `{ClaimLink}` - LNURL for claiming
- `{OrderId}` - Order/transaction ID
- `{StoreName}` - Your store name

#### Example Email Template
```html
Subject: You earned {RewardAmountSatoshis} sats!

Hi there,

Thank you for your purchase from {StoreName}!

You've earned a Bitcoin reward:
💰 {RewardAmountSatoshis} satoshis ({RewardAmountBTC} BTC)

To claim your reward:
1. Open your Lightning wallet
2. Scan this QR code or paste the LNURL:

{ClaimLink}

Questions? Visit our website or reply to this email.

Thanks for choosing Bitcoin!
{StoreName}
```

### Testing Email Delivery

1. Create a test reward (see Testing section)
2. Check email inbox
3. Verify all tokens rendered correctly
4. Test claim link works

---

## Testing

### Test Controller (Development Only)

The plugin includes a test controller for development (disabled in production builds).

#### Create Test Reward via UI

1. **Stores** → [Your Store] → **Plugins** → **Bitcoin Rewards** → **Rewards History**
2. Click **"Create Test Reward"** button
3. Fill in:
   - Amount: `5`
   - Currency: `CAD` (or your currency)
   - Platform: `Square`
   - Email: `test@example.com`
4. Click **"Create Test Reward"**
5. Check status and logs

### Manual Testing Checklist

- [ ] Create test reward succeeds
- [ ] Reward appears in history
- [ ] Display page loads with QR code
- [ ] Email notification sent (if enabled)
- [ ] Claim link works in Lightning wallet
- [ ] Pull payment deducts from store balance
- [ ] Webhook signature verification passes (Square)

### Testing Square Webhook

#### Option 1: Square Sandbox
1. Use Square sandbox environment
2. Create test payment
3. Webhook triggers automatically

#### Option 2: Manual cURL Test
```bash
curl -X POST https://your-btcpay-domain.com/plugins/bitcoin-rewards/YOUR_STORE_ID/webhooks/square \
  -H "Content-Type: application/json" \
  -H "X-Square-Signature: test-signature" \
  -d '{
    "type": "payment.updated",
    "data": {
      "object": {
        "payment": {
          "id": "test123",
          "amount_money": {
            "amount": 500,
            "currency": "CAD"
          },
          "status": "COMPLETED"
        }
      }
    }
  }'
```

---

## Best Practices

### Security

1. **Always use HTTPS** - Required for Square webhooks
2. **Keep signature keys secret** - Never commit to git
3. **Rotate keys periodically** - Update in both Square and BTCPay
4. **Monitor logs** - Check for unauthorized webhook attempts

### Performance

1. **Enable database cleanup** - Auto-delete old claimed rewards
2. **Set reasonable expiry** - 7-30 days for pull payments
3. **Use rate caching** - Configure in BTCPay rate settings
4. **Monitor disk space** - PostgreSQL database growth

### User Experience

1. **Clear instructions** - Explain how to claim rewards
2. **Test email templates** - Verify rendering on mobile
3. **Provide support** - Help customers with Lightning wallets
4. **Set realistic percentages** - Start with 1-2% rewards

### Operational

1. **Monitor Lightning liquidity** - Ensure inbound capacity
2. **Track reward costs** - Monitor total satoshis distributed
3. **Review analytics** - Check claim rates and conversions
4. **Update regularly** - Keep plugin up to date

### Reward Percentages by Industry

| Industry | Typical Range | Notes |
|----------|---------------|-------|
| Coffee Shop | 2-5% | Frequent small purchases |
| Restaurant | 1-3% | Higher ticket amounts |
| Retail Store | 1-2% | Varies by margin |
| Online Store | 0.5-2% | Lower margins |
| Professional Services | 0.5-1% | High ticket items |

---

## Troubleshooting

For common issues and solutions, see [TROUBLESHOOTING.md](./TROUBLESHOOTING.md)

---

## Support

- **Documentation**: https://github.com/jpgaviria2/bitcoinrewards
- **Issues**: https://github.com/jpgaviria2/bitcoinrewards/issues
- **BTCPay Community**: https://chat.btcpayserver.org

---

## Next Steps

1. ✅ Complete initial setup
2. ✅ Test with small amounts
3. ✅ Configure email notifications
4. ✅ Customize display page
5. ✅ Set up Square webhook (if using)
6. ✅ Launch to customers!

Happy rewarding! ⚡🎉
