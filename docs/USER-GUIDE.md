# Bitcoin Rewards Plugin - User Guide

## For Store Owners and Administrators

This guide will help you set up and manage Bitcoin rewards for your customers using BTCPay Server.

---

## Table of Contents

1. [Introduction](#introduction)
2. [Installation](#installation)
3. [Initial Setup](#initial-setup)
4. [Square Integration](#square-integration)
5. [Display Configuration](#display-configuration)
6. [Email Notifications](#email-notifications)
7. [Monitoring and Troubleshooting](#monitoring-and-troubleshooting)
8. [Advanced Configuration](#advanced-configuration)

---

## Introduction

### What is Bitcoin Rewards?

The Bitcoin Rewards plugin automatically sends Bitcoin rewards to your customers when they make purchases. It integrates with Square (and other platforms) to detect payments and send Lightning Network rewards via BTCPay Server.

### Key Features

- ✅ **Automatic Rewards** - Send Bitcoin to customers after purchase
- ✅ **Square Integration** - Works with Square POS systems
- ✅ **Customizable Branding** - Match your store's look and feel
- ✅ **Customer-Facing Display** - Show rewards on a tablet or display
- ✅ **Email Notifications** - Send claim links via email
- ✅ **Production-Grade** - Error tracking, metrics, auto-recovery

### How It Works

```
1. Customer pays with credit card at Square terminal
2. Square sends webhook to BTCPay Server
3. Plugin calculates reward (e.g., 5% of purchase)
4. Plugin creates Lightning payment request
5. Customer receives email with claim link
6. Customer claims Bitcoin to their wallet
```

---

## Installation

### Prerequisites

- BTCPay Server v2.3.0 or later
- Lightning Network node (LND or Core Lightning)
- Square account (for payment integration)

### Install Plugin

1. **Via BTCPay UI:**
   - Settings → Plugins → Available Plugins
   - Find "Bitcoin Rewards"
   - Click "Install"
   - Restart BTCPay Server

2. **Manual Installation:**
   ```bash
   # Download plugin
   wget https://github.com/jpgaviria2/bitcoinrewards/releases/latest/download/BTCPayServer.Plugins.BitcoinRewards.zip
   
   # Extract to plugins directory
   unzip BTCPayServer.Plugins.BitcoinRewards.zip -d /path/to/btcpay/Plugins/BTCPayServer.Plugins.BitcoinRewards/
   
   # Restart BTCPay
   docker restart btcpayserver
   ```

3. **Verify Installation:**
   - Navigate to your store
   - Sidebar should show "Bitcoin Rewards" menu item

---

## Initial Setup

### Step 1: Enable Plugin

1. Navigate to: **Store → Bitcoin Rewards → Settings**
2. Toggle **"Enable Bitcoin Rewards"** to ON
3. Click **Save**

### Step 2: Configure Reward Percentage

**External Platforms (Square):**
- Set the percentage of purchase amount to reward
- Example: 5% means $100 purchase = $5 Bitcoin reward
- Recommended: 2-5% for most businesses

**BTCPay Invoices:**
- Separate percentage for BTCPay-native payments
- Can be higher to incentivize Bitcoin adoption
- Recommended: 5-10%

### Step 3: Set Minimum Transaction Amount

**Purpose:** Prevent processing tiny rewards (dust)

```
Minimum Transaction: $5.00
```

Small purchases below this threshold won't generate rewards.

### Step 4: Set Maximum Reward Cap

**Security Feature:** Prevents fraudulent large rewards

```
Maximum Single Reward: 100,000 sats (~$50 at $50k/BTC)
```

**Recommendation:** Set based on your risk tolerance and average transaction size.

---

## Square Integration

### Overview

Square integration allows automatic reward creation when customers pay with credit/debit cards at your Square terminal.

### Step 1: Get Square API Credentials

1. Log in to [Square Developer Dashboard](https://developer.squareup.com/)
2. Create new application or select existing
3. Note your **Application ID**
4. Generate **Access Token** (Production)
5. Copy **Webhook Signature Key** from Webhooks settings

### Step 2: Configure in BTCPay

1. **Store → Bitcoin Rewards → Settings**
2. Scroll to **Square Integration**
3. Enter credentials:
   ```
   Application ID: sq0idp-xxxxxxxxxxxxx
   Access Token: EAAA...
   Webhook Signature Key: xxxxxxxxxxxxx
   ```
4. Click **Save**

### Step 3: Register Webhook

1. In Square Dashboard → Webhooks
2. Click **Add Endpoint**
3. Enter webhook URL:
   ```
   https://your-btcpay-domain.com/plugins/bitcoin-rewards/YOUR_STORE_ID/webhooks/square
   ```
4. Subscribe to event: **`payment.updated`**
5. Click **Save**

### Step 4: Test Integration

1. Make a test payment at Square terminal
2. Check **Bitcoin Rewards → Error Dashboard**
3. If successful, you'll see reward created
4. Check customer email for claim link

**Common Issues:**
- ❌ **401 Unauthorized** → Webhook signature mismatch (check signature key)
- ❌ **No reward created** → Check minimum transaction amount
- ❌ **Email not sent** → Verify email plugin is installed

---

## Display Configuration

### Customer-Facing Display

Show rewards on a tablet or display near your register.

### Step 1: Get Display URL

```
https://your-btcpay-domain.com/plugins/bitcoin-rewards/YOUR_STORE_ID/display
```

Replace `YOUR_STORE_ID` with your actual store ID (found in store settings).

### Step 2: Configure Display Settings

**Timeframe:**
- How far back to search for unclaimed rewards
- Default: 60 minutes
- Recommendation: 30-60 minutes

**Auto-Refresh:**
- How often to check for new rewards
- Default: 10 seconds
- Recommendation: 5-15 seconds

**URL Parameters:**
```
?timeframeMinutes=60&autoRefreshSeconds=10
```

### Step 3: Customize Branding

1. **Store → Bitcoin Rewards → Settings**
2. Scroll to **Display Template**
3. Customize:
   - **Primary Color** - Main brand color (e.g., #6B4423 for brown)
   - **Secondary Color** - Accent color (e.g., #CD853F for gold)
   - **Logo URL** - Your store logo
   - **Custom HTML Template** - Advanced customization

**Example:**
```
Primary Color: #6B4423
Secondary Color: #CD853F
Accent Color: #F5F5DC
Logo URL: https://your-domain.com/logo.png
```

### Step 4: Set Up Display Hardware

**Recommended Hardware:**
- Android tablet (8-10 inches)
- iPad (alternative)
- Raspberry Pi with touchscreen (budget option)

**Setup:**
1. Open web browser on device
2. Navigate to display URL
3. Enable kiosk mode (full screen)
4. Set browser to auto-start on boot

**Kiosk Mode:**
- **Chrome:** F11 or Settings → More Tools → Extensions → Kiosk
- **iPad:** Guided Access (Settings → Accessibility)

---

## Email Notifications

### Enable Email Plugin

Bitcoin Rewards uses BTCPay's email plugin to send notifications.

1. **Server Settings → Emails**
2. Configure SMTP:
   ```
   SMTP Server: smtp.gmail.com
   Port: 587
   Username: your-email@gmail.com
   Password: app-password
   ```
3. Send test email to verify

### Customize Email Template

1. **Store → Bitcoin Rewards → Settings**
2. Scroll to **Email Template**
3. Edit HTML template (supports tokens):
   ```
   {STORE_NAME} - Your store name
   {REWARD_AMOUNT_BTC} - Amount in BTC
   {REWARD_AMOUNT_SATS} - Amount in satoshis
   {CLAIM_LINK} - LNURL claim link
   {ORDER_ID} - Order reference
   ```

**Example:**
```html
<p>Hi there!</p>
<p>Thanks for shopping at {STORE_NAME}!</p>
<p>You've earned {REWARD_AMOUNT_SATS} sats (~${REWARD_AMOUNT_USD}).</p>
<p><a href="{CLAIM_LINK}">Claim Your Bitcoin Reward</a></p>
```

### Email Subject

Customize the email subject line:
```
🎁 Your Bitcoin Reward from {STORE_NAME}
```

---

## Monitoring and Troubleshooting

### Error Dashboard

View and resolve errors:

1. **Store → Bitcoin Rewards → Errors**
2. See statistics:
   - Total errors
   - Unresolved errors
   - Retryable errors
3. Filter by:
   - Time range (1-90 days)
   - Resolution status

**Actions:**
- Click **Details** to see full stack trace
- Click **Resolve** to mark as fixed

### Metrics Dashboard

Monitor plugin health:

1. **Store → Bitcoin Rewards → Metrics** (coming in Phase 4)
2. Or use Prometheus endpoint:
   ```
   https://your-domain.com/api/v1/bitcoin-rewards/metrics
   ```

**Key Metrics:**
- Rewards created per day
- Claim rate percentage
- Average reward amount
- Error rate

### Common Issues

#### "Reward created but email not sent"

**Cause:** Email plugin not configured

**Solution:**
1. Configure SMTP settings (Server Settings → Emails)
2. Test email delivery
3. Check spam folder

#### "Payment webhook not received"

**Cause:** Webhook not registered or signature mismatch

**Solution:**
1. Verify webhook URL in Square dashboard
2. Check webhook signature key matches
3. Test webhook with Square's testing tool

#### "Reward amount is 0 sats"

**Cause:** Transaction below minimum or percentage misconfigured

**Solution:**
1. Check minimum transaction amount setting
2. Verify reward percentage (should be > 0)
3. Check currency conversion rates

#### "Lightning node offline"

**Cause:** LND/Core Lightning node unavailable

**Solution:**
1. Check Lightning node status
2. Restart Lightning node if needed
3. Auto-recovery will retry failed rewards

---

## Advanced Configuration

### Rate Limiting

Prevent abuse by limiting webhook requests:

1. **Store → Bitcoin Rewards → Rate Limits**
2. Configure limits:
   ```
   Webhook: 60 req/min
   API: 120 req/min
   Admin: 300 req/min
   ```
3. Add IP whitelist/blacklist

### Custom Waiting Template

Advanced HTML/CSS customization:

```html
<div style="text-align: center; padding: 50px;">
  <img src="{LOGO_URL}" style="max-width: 200px;" />
  <h1 style="color: {PRIMARY_COLOR};">Welcome to {STORE_NAME}</h1>
  <p style="color: {SECONDARY_COLOR};">Waiting for your next reward...</p>
</div>
```

### Multiple Stores

Run Bitcoin Rewards on multiple stores:

1. Each store has independent settings
2. Separate webhook URLs per store
3. Individual rate limits and branding

### Backup and Recovery

**Automatic Recovery:**
- Plugin automatically retries failed rewards
- Exponential backoff (5 attempts)
- Escalates to admin after max retries

**Manual Recovery:**
1. **Errors Dashboard** → Find failed reward
2. Click **Retry** or wait for auto-recovery
3. Check Lightning node if persistent failures

---

## Best Practices

### Security

✅ **Always use HTTPS** - Required for webhook signatures  
✅ **Rotate API keys** - Change Square tokens periodically  
✅ **Set reward caps** - Prevent fraudulent large rewards  
✅ **Monitor error logs** - Catch issues early  
✅ **Enable rate limiting** - Prevent DoS attacks  

### Performance

✅ **Optimize display refresh** - 10-15 seconds is ideal  
✅ **Cache settings** - Plugin caches for performance  
✅ **Monitor metrics** - Track reward claim rates  

### Customer Experience

✅ **Clear branding** - Use your logo and colors  
✅ **Simple language** - "Claim your Bitcoin" not "LNURL withdrawal"  
✅ **Test customer flow** - Make a real purchase and claim reward  
✅ **QR codes** - Display QR on tablet for easy mobile scanning  

---

## Support and Resources

### Documentation
- [API Reference](./API-REFERENCE.md)
- [Admin Guide](./ADMIN-GUIDE.md)
- [Testing Guide](../TESTING-GUIDE.md)

### Community
- **GitHub Issues:** https://github.com/jpgaviria2/bitcoinrewards/issues
- **Discussions:** https://github.com/jpgaviria2/bitcoinrewards/discussions
- **BTCPay Community:** https://chat.btcpayserver.org/

### Professional Support
For production deployments, consider professional support for:
- Custom integrations
- Multi-store setups
- Performance optimization
- Security audits

---

**Last Updated:** 2026-03-28  
**Version:** 1.4.1  
**Plugin Version:** Compatible with BTCPay Server 2.3.0+
