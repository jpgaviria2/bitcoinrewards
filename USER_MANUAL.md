# Bitcoin Rewards Plugin - User Manual

## Table of Contents

1. [Introduction](#introduction)
2. [Installation](#installation)
3. [Getting Started](#getting-started)
4. [Configuration](#configuration)
5. [Webhook Setup](#webhook-setup)
6. [Email Configuration](#email-configuration)
7. [Wallet Settings](#wallet-settings)
8. [Viewing Rewards](#viewing-rewards)
10. [How It Works](#how-it-works)
11. [Troubleshooting](#troubleshooting)
12. [Best Practices](#best-practices)
13. [FAQ](#faq)
14. [Security Considerations](#security-considerations)

---

## Introduction

The **Bitcoin Rewards Plugin** for BTCPay Server enables merchants to automatically reward customers with Bitcoin when they make purchases through Shopify. This creates a seamless loyalty program that incentivizes repeat purchases while introducing customers to Bitcoin.

### Key Features

- **Automatic Reward Calculation**: Automatically calculates Bitcoin rewards based on a configurable percentage of order amounts
- **Shopify Integration**: Works with Shopify e-commerce platform
- **Secure Webhook Processing**: Verifies webhook signatures to ensure only legitimate orders are processed
- **Email Notifications**: Sends automated email notifications to customers with their reward details
- **Flexible Wallet Options**: Supports Lightning Network, eCash, and on-chain Bitcoin payments
- **Currency Conversion**: Automatically converts fiat currency amounts to Bitcoin using real-time exchange rates
- **Reward Limits**: Set minimum order amounts and maximum reward caps to control costs
- **Reward History**: Track all rewards sent to customers with detailed status information

### Use Cases

- **Loyalty Programs**: Reward repeat customers with Bitcoin
- **Cashback Programs**: Give customers Bitcoin cashback on purchases
- **Referral Rewards**: Incentivize customers to refer friends
- **Promotional Campaigns**: Run special promotions with Bitcoin rewards
- **Bitcoin Adoption**: Introduce customers to Bitcoin through rewards

---

## Installation

### Prerequisites

- BTCPay Server 2.0.0 or later
- A BTCPay Server store configured
- (Optional) BTCPay Server Emails plugin for email notifications
- (Optional) BTCPay Server Lightning node for Lightning rewards

### Installation Methods

#### Method 1: Plugin Builder (Recommended)

1. Log in to your BTCPay Server instance
2. Navigate to **Server Settings** → **Plugins**
3. Search for "Bitcoin Rewards" in the plugin marketplace
4. Click **Install** next to the Bitcoin Rewards plugin
5. Wait for the installation to complete
6. The plugin will be automatically enabled

#### Method 2: Manual Installation

1. Download the latest `.btcpay` file from the [BTCPay Server Plugin Builder](https://plugin-builder.btcpayserver.org/)
2. Copy the `.btcpay` file to your BTCPay Server plugins directory:
   - **Docker Installation**: `/btcpay/plugins/`
   - **Manual Installation**: `{BTCPayServerDirectory}/Plugins/`
3. Restart BTCPay Server:
   - **Docker**: `docker restart btcpayserver`
   - **Manual**: Restart the BTCPay Server service
4. The plugin will appear in your store settings

### Verification

After installation, verify the plugin is active:

1. Go to your store settings
2. Look for **"Bitcoin Rewards"** in the navigation menu
3. If you see it, the plugin is installed and ready to configure

---

## Getting Started

### Initial Setup

1. **Navigate to Store Settings**
   - Go to your BTCPay Server instance
   - Select your store
   - Click on **Store Settings**

2. **Access Bitcoin Rewards**
   - In the store settings navigation, click on **"Bitcoin Rewards"**
   - You'll see the configuration page

3. **Enable the Plugin**
   - Check the **"Enable Bitcoin Rewards"** checkbox
   - Configure at minimum:
     - **Reward Percentage**: The percentage of order amount to give as reward
     - **Webhook Secret**: A secret key for webhook verification
   - Click **Save**

4. **Configure Webhooks**
   - Follow the webhook setup instructions for Shopify
   - Copy the webhook URLs shown in the settings page
   - Configure the webhooks in your e-commerce platform

---

## Configuration

### Basic Settings

#### Enable Bitcoin Rewards

**Location**: Top of the settings page

**Description**: Master toggle to enable/disable the Bitcoin Rewards plugin for this store.

**Usage**: 
- Check to enable the plugin
- Uncheck to disable (rewards will not be processed, but settings are preserved)

#### Reward Percentage

**Location**: Basic Settings section

**Description**: The percentage of the order amount that will be given as a Bitcoin reward.

**Format**: Decimal number (e.g., 0.01 = 1%, 0.05 = 5%)

**Default**: 0.01 (1%)

**Example**:
- Order amount: $100.00
- Reward percentage: 0.01 (1%)
- Reward amount: $1.00 (converted to BTC at current exchange rate)

**Recommendations**:
- Start with 0.01 (1%) to test the system
- Consider your profit margins when setting higher percentages
- Monitor reward costs regularly

#### Minimum Order Amount

**Location**: Basic Settings section

**Description**: The minimum order amount (in fiat currency) required for a customer to receive a reward.

**Format**: Decimal number (e.g., 10.00 = $10.00)

**Default**: 0.00 (no minimum)

**Example**:
- Minimum order amount: $25.00
- Order amount: $20.00 → No reward
- Order amount: $30.00 → Reward given

**Use Cases**:
- Prevent rewards on very small orders
- Encourage larger purchases
- Control reward costs

#### Maximum Reward Amount

**Location**: Basic Settings section

**Description**: The maximum Bitcoin reward amount (in BTC) that can be given per order.

**Format**: Decimal number in BTC (e.g., 0.001 = 0.001 BTC)

**Default**: 1000.00 BTC (effectively unlimited for most use cases)

**Example**:
- Maximum reward: 0.001 BTC (~$50 at $50k/BTC)
- Calculated reward: 0.002 BTC → Capped at 0.001 BTC

**Use Cases**:
- Cap rewards on very large orders
- Control maximum reward costs
- Prevent excessive rewards during promotions

### Integration Settings

#### Shopify Enabled

**Location**: Integration Settings section

**Description**: Enable or disable Shopify webhook processing.

**Usage**:
- Check to enable Shopify integration
- Uncheck to disable (Shopify webhooks will be ignored)

**Requirements**:
- Shopify store with webhook configured
- Valid webhook secret

#### Webhook Secret

**Location**: Integration Settings section

**Description**: Secret key used to verify webhook signatures from Shopify.

**Security**: This is a critical security setting. The webhook secret ensures that only legitimate webhooks from Shopify are processed.

**How to Get**:
- **Shopify**: Generated when creating a webhook

**Best Practices**:
- Use a strong, random secret (at least 32 characters)
- Never share this secret publicly
- Use different secrets for different stores
- Rotate secrets periodically

**Format**: Any string (recommended: random alphanumeric string)

---

## Webhook Setup

### Shopify Webhook Configuration

#### Step 1: Get Your Webhook URL

1. Go to your store settings in BTCPay Server
2. Navigate to **Bitcoin Rewards** settings
3. Find the **Webhook URLs** section
4. Copy the **Shopify Webhook URL** (format: `https://your-btcpay-server.com/plugins/bitcoinrewards/webhooks/shopify/{storeId}`)

#### Step 2: Create Webhook in Shopify

1. Log in to your Shopify Admin
2. Navigate to **Settings** → **Notifications**
3. Scroll down to **Webhooks** section
4. Click **Create webhook**
5. Configure the webhook:
   - **Event**: Select **Order creation**
   - **Format**: Select **JSON**
   - **URL**: Paste the webhook URL from Step 1
   - **API version**: Select the latest version
6. Click **Save webhook**

#### Step 3: Get Webhook Secret

1. After creating the webhook, Shopify will display a **Signing secret**
2. Copy this secret
3. Go back to BTCPay Server Bitcoin Rewards settings
4. Paste the secret into the **Webhook Secret** field
5. Check **Shopify Enabled**
6. Click **Save**

#### Step 4: Test the Webhook

1. Create a test order in your Shopify store
2. Check the BTCPay Server logs or rewards history
3. Verify the webhook was received and processed

**Troubleshooting**:
- If webhooks aren't being received, check:
  - Webhook URL is correct and accessible
  - Webhook secret matches
  - Shopify webhook is active
  - BTCPay Server is accessible from the internet


## Email Configuration

### Email Settings Overview

The plugin can send email notifications to customers when they receive Bitcoin rewards. Email functionality requires the **BTCPay Server Emails plugin** to be installed.

### Email Configuration Options

#### Email From Address

**Location**: Email Settings section

**Description**: The email address that will appear as the sender of reward notification emails.

**Format**: Valid email address (e.g., `rewards@yourstore.com`)

**Default**: `noreply@btcpayserver.org`

**Recommendations**:
- Use a professional email address
- Ensure the domain is properly configured
- Consider using a subdomain like `rewards@yourstore.com`

#### Email Subject

**Location**: Email Settings section

**Description**: The subject line of reward notification emails.

**Default**: "Your Bitcoin Reward is Ready!"

**Customization Examples**:
- "🎉 You've Earned Bitcoin Rewards!"
- "Your Bitcoin Reward from [Store Name]"
- "Congratulations! Your Bitcoin is Here"

#### Email Body Template

**Location**: Email Settings section

**Description**: The body content of reward notification emails. Supports placeholders that will be replaced with actual values.

**Default Template**:
```
Congratulations! You've earned {RewardAmount} BTC as a reward for your purchase. Your reward has been sent to: {BitcoinAddress}
```

### Email Template Variables

The following placeholders can be used in the email body template:

| Placeholder | Description | Example |
|------------|-------------|---------|
| `{RewardAmount}` | The Bitcoin reward amount in BTC | `0.00012345` |
| `{BitcoinAddress}` | The Bitcoin address where the reward was sent | `bc1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh` |
| `{OrderId}` | The order ID from Shopify | `#12345` |
| `{TransactionId}` | The Bitcoin transaction ID (if available) | `abc123def456...` or `pending` |

### Email Template Examples

#### Simple Template
```
Thank you for your purchase!

You've earned {RewardAmount} BTC as a reward.

Your Bitcoin has been sent to: {BitcoinAddress}

Order: {OrderId}
```

#### Detailed Template
```
Hello!

Thank you for shopping with us. As a token of appreciation, we've sent you a Bitcoin reward!

Reward Details:
- Amount: {RewardAmount} BTC
- Bitcoin Address: {BitcoinAddress}
- Order Number: {OrderId}
- Transaction ID: {TransactionId}

Your Bitcoin reward is now in your wallet. Thank you for being a valued customer!

Best regards,
[Your Store Name]
```

#### HTML Template (if supported)
```
<h2>🎉 You've Earned Bitcoin Rewards!</h2>
<p>Thank you for your purchase!</p>
<p><strong>Reward Amount:</strong> {RewardAmount} BTC</p>
<p><strong>Bitcoin Address:</strong> <code>{BitcoinAddress}</code></p>
<p><strong>Order ID:</strong> {OrderId}</p>
```

### Email Requirements

- **BTCPay Server Emails Plugin**: Must be installed and configured
- **Email Server**: BTCPay Server must have email sending configured
- **Customer Email**: Customers must have an email address in their order

### Email Troubleshooting

**Issue**: Emails are not being sent

**Solutions**:
1. Verify BTCPay Server Emails plugin is installed
2. Check email server configuration in BTCPay Server
3. Verify customer email addresses are valid
4. Check BTCPay Server logs for email errors
5. Test email sending from BTCPay Server directly

**Issue**: Email template variables not replaced

**Solutions**:
1. Ensure placeholders use exact format: `{VariableName}`
2. Check for typos in placeholder names
3. Verify order data contains the required information

---

## Wallet Settings

### Wallet Preference

**Location**: Wallet Settings section

**Description**: Determines which type of Bitcoin wallet to use when sending rewards, and the fallback order if the preferred method is unavailable.

**Options**:

1. **Lightning First**
   - Tries Lightning Network first
   - Falls back to eCash if Lightning unavailable
   - Falls back to on-chain if eCash unavailable
   - **Best for**: Fast, low-fee rewards

2. **eCash First**
   - Tries eCash first
   - Falls back to Lightning if eCash unavailable
   - Falls back to on-chain if Lightning unavailable
   - **Best for**: Low-fee rewards with Lightning backup

3. **On-chain Only**
   - Only uses on-chain Bitcoin transactions
   - No fallback to Lightning or eCash
   - **Best for**: Maximum compatibility, traditional Bitcoin

**Recommendations**:
- Use **Lightning First** for small rewards (< $10)
- Use **On-chain Only** for larger rewards or maximum compatibility
- Test your preferred method before going live

### Preferred Lightning Node ID

**Location**: Wallet Settings section

**Description**: (Optional) If you have multiple Lightning nodes configured in BTCPay Server, specify which one to use for rewards.

**Format**: The Lightning node ID as configured in BTCPay Server

**Usage**:
- Leave empty to use the default Lightning node
- Enter a specific node ID to use that node
- Only applicable when Lightning is the preferred or fallback method

**How to Find Node ID**:
1. Go to BTCPay Server → Server Settings → Lightning
2. Find your Lightning node
3. Copy the Node ID

---


## Viewing Rewards

### Accessing Reward History

1. Go to your store settings
2. Navigate to **Bitcoin Rewards**
3. Click on **View Rewards** (or navigate to the rewards list)

### Reward Information Displayed

The rewards view shows:

- **Reward ID**: Unique identifier for the reward
- **Order ID**: The order ID from Shopify
- **Customer Email**: Customer's email address
- **Customer Phone**: Customer's phone number (if available)
- **Reward Amount**: Bitcoin amount in BTC
- **Bitcoin Address**: Address where reward was sent
- **Transaction ID**: Bitcoin transaction ID (if sent)
- **Status**: Current status of the reward
- **Created At**: When the reward was created
- **Sent At**: When the reward was sent (if applicable)
- **Source**: Platform source (Shopify)

### Reward Statuses

- **Pending**: Reward created but not yet processed
- **Processing**: Reward is being processed (Bitcoin being sent)
- **Sent**: Reward successfully sent to customer
- **Failed**: Reward processing failed (check logs for details)

### Filtering and Searching

(If implemented in the view)
- Filter by status
- Search by order ID or customer email
- Filter by date range
- Filter by source (Shopify)

---

## How It Works

### Reward Processing Flow

1. **Order Received**
   - Customer places order in Shopify
   - Shopify sends webhook to BTCPay Server

2. **Webhook Verification**
   - Plugin verifies webhook signature using webhook secret
   - Ensures webhook is legitimate and from Shopify

3. **Order Data Parsing**
   - Plugin extracts order information:
     - Order ID and number
     - Order amount and currency
     - Customer email and/or phone
     - Customer name
   - May fetch additional customer data via Shopify API if needed

4. **Reward Calculation**
   - Calculates reward amount: `Order Amount × Reward Percentage`
   - Applies minimum order amount check
   - Applies maximum reward amount cap
   - Converts fiat amount to BTC using exchange rate

5. **Bitcoin Address Management**
   - Checks if customer has existing Bitcoin address
   - Reuses address if available (for returning customers)
   - Generates new address if needed

6. **Bitcoin Sending**
   - Sends Bitcoin according to wallet preference:
     - Lightning Network (if preferred and available)
     - eCash (if preferred or as fallback)
     - On-chain (if preferred or as final fallback)
   - Records transaction ID

7. **Email Notification**
   - Sends email to customer with reward details
   - Includes Bitcoin address and transaction information

8. **Record Keeping**
   - Saves reward record to database
   - Updates status as processing progresses
   - Logs all actions for auditing

### Currency Conversion

The plugin automatically converts fiat currency amounts to Bitcoin:

1. **Exchange Rate Lookup**
   - Uses configured exchange rate provider (CoinGecko, BitFlyer, etc.)
   - Fetches current exchange rate for order currency → BTC

2. **Rate Application**
   - Applies exchange rate to calculated reward amount
   - Handles rate lookup failures with fallback rates

3. **Fallback Behavior**
   - If exchange rate unavailable, uses fallback rate
   - Logs warnings for manual review

### Error Handling

The plugin includes robust error handling:

- **Webhook Verification Failures**: Webhook rejected, logged
- **Order Data Issues**: Reward not created, error logged
- **Bitcoin Sending Failures**: Retry with exponential backoff (up to 3 attempts)
- **Email Sending Failures**: Logged but doesn't block reward processing
- **Exchange Rate Failures**: Uses fallback rate, continues processing

---

## Troubleshooting

### Common Issues and Solutions

#### Issue: Rewards Not Being Created

**Symptoms**:
- Orders placed but no rewards appear in history
- Webhooks received but not processed

**Possible Causes & Solutions**:

1. **Plugin Not Enabled**
   - Check "Enable Bitcoin Rewards" is checked
   - Verify settings are saved

2. **Webhook Not Configured**
   - Verify webhook URL is correct in e-commerce platform
   - Check webhook is active in Shopify
   - Verify webhook secret matches

3. **Webhook Verification Failing**
   - Check webhook secret is correct
   - Verify webhook signature in logs
   - Ensure webhook URL is accessible from internet

4. **Order Below Minimum**
   - Check minimum order amount setting
   - Verify order amount meets requirement

5. **Integration Not Enabled**
   - Check "Shopify Enabled" is checked
   - Verify Shopify integration is enabled

#### Issue: Rewards Created But Not Sent

**Symptoms**:
- Rewards appear in history with "Pending" or "Processing" status
- Bitcoin not actually sent to customers

**Possible Causes & Solutions**:

1. **Wallet Not Configured**
   - Verify BTCPay Server wallet is set up
   - Check wallet has sufficient balance
   - Ensure wallet is connected and synced

2. **Lightning Node Issues** (if using Lightning)
   - Check Lightning node is online
   - Verify node has sufficient capacity
   - Check Lightning node configuration

3. **Insufficient Funds**
   - Verify wallet has enough Bitcoin
   - Check for transaction fees
   - Ensure wallet balance covers rewards + fees

4. **Address Generation Failing**
   - Check wallet service is accessible
   - Verify derivation scheme is configured
   - Check BTCPay Server logs for address generation errors

#### Issue: Emails Not Being Sent

**Symptoms**:
- Rewards sent but customers not receiving emails
- Email status unknown

**Possible Causes & Solutions**:

1. **Emails Plugin Not Installed**
   - Install BTCPay Server Emails plugin
   - Configure email server settings

2. **Email Server Not Configured**
   - Configure SMTP settings in BTCPay Server
   - Test email sending from BTCPay Server
   - Verify email server credentials

3. **Invalid Customer Email**
   - Check customer email addresses are valid
   - Verify email format in order data
   - Check for email typos

4. **Email Template Issues**
   - Verify email template is configured
   - Check for template syntax errors
   - Test email template with sample data

#### Issue: Incorrect Reward Amounts

**Symptoms**:
- Rewards calculated incorrectly
- Amounts don't match expected percentage

**Possible Causes & Solutions**:

1. **Reward Percentage Setting**
   - Verify reward percentage is set correctly
   - Check for decimal vs percentage confusion (0.01 = 1%, not 0.01%)

2. **Maximum Reward Cap**
   - Check if maximum reward amount is capping rewards
   - Verify maximum is set appropriately

3. **Exchange Rate Issues**
   - Check exchange rate provider is working
   - Verify exchange rate is current
   - Review exchange rate in logs

4. **Currency Conversion**
   - Verify order currency is supported
   - Check exchange rate for order currency → BTC
   - Review conversion calculations in logs

#### Issue: Webhook Signature Verification Failing

**Symptoms**:
- Webhooks received but rejected
- "Invalid webhook signature" errors in logs

**Possible Causes & Solutions**:

1. **Webhook Secret Mismatch**
   - Verify webhook secret in plugin matches e-commerce platform
   - Re-copy secret from platform
   - Ensure no extra spaces or characters

2. **Webhook URL Mismatch**
   - Verify webhook URL in platform matches BTCPay Server
   - Check for HTTPS vs HTTP differences
   - Ensure URL includes correct store ID

3. **Platform-Specific Issues**
   - **Shopify**: Verify HMAC SHA256 signature format
   - Check Shopify documentation for signature requirements

### Checking Logs

BTCPay Server logs contain detailed information about plugin operations:

1. **Access Logs**
   - Docker: `docker logs btcpayserver`
   - Manual: Check BTCPay Server log directory

2. **What to Look For**
   - Webhook receipt confirmations
   - Reward processing steps
   - Error messages and stack traces
   - Bitcoin transaction details
   - Email sending status

3. **Log Levels**
   - **Information**: Normal operations
   - **Warning**: Non-critical issues (e.g., fallback rates)
   - **Error**: Critical failures requiring attention

### Getting Help

If you're unable to resolve an issue:

1. **Check Documentation**
   - Review this manual
   - Check BTCPay Server documentation
   - Review plugin README

2. **Review Logs**
   - Check BTCPay Server logs for errors
   - Look for specific error messages
   - Note timestamps of issues

3. **Community Support**
   - BTCPay Server Discord/Forum
   - GitHub Issues (if open source)
   - Community plugins discussion

4. **Report Issues**
   - Include error messages from logs
   - Describe steps to reproduce
   - Include relevant configuration (sanitized)

---

## Best Practices

### Security

1. **Webhook Secrets**
   - Use strong, random secrets (32+ characters)
   - Never share secrets publicly
   - Rotate secrets periodically
   - Use different secrets for different stores

2. **API Credentials**
   - Keep Shopify API credentials secure
   - Never commit credentials to version control
   - Rotate access tokens regularly
   - Use least-privilege access when possible

3. **Email Addresses**
   - Use professional email addresses
   - Verify email domain configuration
   - Monitor for email delivery issues

### Reward Configuration

1. **Start Conservative**
   - Begin with low reward percentages (0.5-1%)
   - Test with small orders first
   - Monitor costs before scaling

2. **Set Appropriate Limits**
   - Use minimum order amounts to control costs
   - Set maximum rewards to cap large orders
   - Review and adjust limits regularly

3. **Monitor Costs**
   - Track total rewards given
   - Monitor Bitcoin price fluctuations
   - Adjust percentages based on profitability

### Testing

1. **Test Webhooks**
   - Test with small test orders first
   - Verify webhook signatures work
   - Confirm rewards are calculated correctly

2. **Test Email Delivery**
   - Send test emails to yourself
   - Verify email templates render correctly
   - Check all placeholders are replaced

3. **Test Bitcoin Sending**
   - Start with very small amounts
   - Verify transactions appear on blockchain
   - Test Lightning payments if using Lightning

### Monitoring

1. **Regular Reviews**
   - Review reward history regularly
   - Check for failed rewards
   - Monitor for unusual patterns

2. **Cost Tracking**
   - Track total rewards given
   - Monitor Bitcoin price impact
   - Calculate reward costs vs. revenue

3. **Customer Feedback**
   - Monitor customer inquiries about rewards
   - Track email delivery success
   - Address customer concerns promptly

### Performance

1. **Webhook Processing**
   - Monitor webhook processing times
   - Check for webhook backlogs
   - Optimize if processing is slow

2. **Database Maintenance**
   - Regularly review reward records
   - Archive old records if needed
   - Monitor database size

---

## FAQ

### General Questions

**Q: Does this plugin work with other e-commerce platforms besides Shopify?**
A: Currently, the plugin only supports Shopify integration. Support for additional platforms may be added in future versions.

**Q: Do customers need a Bitcoin wallet to receive rewards?**
A: Yes, customers need a Bitcoin address to receive rewards. The plugin generates addresses, but customers should have a wallet to access their Bitcoin.

**Q: Can I customize the reward percentage per customer?**
A: Currently, the reward percentage is set globally per store. Per-customer customization is not available in this version.

**Q: What happens if a customer doesn't have an email address?**
A: The plugin requires either an email address or phone number. If neither is available, the reward cannot be processed.

**Q: Can I manually send rewards?**
A: Currently, rewards are only sent automatically via webhooks. Manual reward sending is not available in this version.

### Technical Questions

**Q: What Bitcoin networks are supported?**
A: The plugin supports:
- Lightning Network (if Lightning node configured)
- eCash (if eCash wallet configured)
- On-chain Bitcoin (mainnet)

**Q: How are exchange rates determined?**
A: Exchange rates are fetched from the configured provider (CoinGecko, BitFlyer, etc.) in real-time when processing orders.

**Q: What happens if the exchange rate provider is down?**
A: The plugin uses a fallback rate (approximately $50,000 USD/BTC) and logs a warning. You should monitor logs for these situations.

**Q: Can I use a custom exchange rate?**
A: Custom exchange rates are not currently supported. The plugin uses the configured exchange rate provider.

**Q: How are Bitcoin addresses generated?**
A: Addresses are generated using BTCPay Server's wallet service. The plugin attempts to reuse addresses for returning customers.

### Integration Questions

**Q: Do I need to configure webhooks for each store?**
A: Yes, each store has its own webhook URL and secret. Configure webhooks separately for each store.

**Q: Can I use the same webhook secret for multiple stores?**
A: While technically possible, it's not recommended for security reasons. Use unique secrets for each store.

**Q: What Shopify events trigger rewards?**
A: Currently, only "Order creation" events are supported. Other events are ignored.

**Q: Can I test webhooks without processing real orders?**
A: Yes, you can create test orders in Shopify test mode to verify webhook processing.

### Cost and Pricing Questions

**Q: How much does this plugin cost?**
A: The plugin itself is free. However, you'll pay:
- Bitcoin transaction fees (when sending rewards)
- Lightning network fees (if using Lightning)
- Exchange rate spreads (when converting fiat to BTC)

**Q: Can I set different reward percentages for different products?**
A: Currently, reward percentage is set globally per store. Product-specific percentages are not available.

**Q: How do I calculate my reward costs?**
A: Reward costs = (Order Amount × Reward Percentage) + Transaction Fees + Exchange Rate Spread

**Q: Can I pause rewards temporarily?**
A: Yes, uncheck "Enable Bitcoin Rewards" to pause all reward processing. Settings are preserved.

### Email Questions

**Q: Do I need the Emails plugin for rewards to work?**
A: No, rewards will still be sent without the Emails plugin. However, customers won't receive email notifications.

**Q: Can I customize email templates?**
A: Yes, you can customize the email subject and body template in the plugin settings. HTML is supported if your email system supports it.

**Q: What if a customer's email bounces?**
A: Email delivery failures are logged but don't prevent reward processing. The reward is still sent to the Bitcoin address.

### Troubleshooting Questions

**Q: Rewards are pending but not being sent. What should I check?**
A: Check:
1. Wallet is configured and has balance
2. Wallet is synced
3. No errors in BTCPay Server logs
4. Bitcoin address generation is working

**Q: How do I know if webhooks are being received?**
A: Check BTCPay Server logs for webhook receipt messages. You can also check the rewards history for new rewards.

**Q: Can I reprocess a failed reward?**
A: Currently, manual reprocessing is not available. Failed rewards remain in "Failed" status. You may need to manually send Bitcoin if needed.

---

## Security Considerations

### Webhook Security

1. **Signature Verification**
   - Always verify webhook signatures
   - Never disable signature verification
   - Use strong, unique webhook secrets

2. **HTTPS Only**
   - Ensure BTCPay Server uses HTTPS
   - Webhook URLs should use HTTPS
   - Never use HTTP for webhooks in production

3. **IP Whitelisting** (if supported)
   - Consider whitelisting Shopify IPs
   - Check Shopify documentation for IP ranges
   - Monitor for unauthorized access attempts

### API Security

1. **Shopify API Credentials**
   - Store credentials securely
   - Never expose in logs or error messages
   - Rotate credentials regularly
   - Use least-privilege access

2. **Access Control**
   - Limit who can modify plugin settings
   - Use BTCPay Server's permission system
   - Review access logs regularly

### Data Privacy

1. **Customer Data**
   - Customer emails/phones are stored for reward processing
   - Follow GDPR/privacy regulations
   - Consider data retention policies
   - Allow customers to request data deletion

2. **Transaction Data**
   - Bitcoin addresses and transaction IDs are stored
   - This data is necessary for reward tracking
   - Consider privacy implications
   - Implement appropriate data retention

### Bitcoin Security

1. **Wallet Security**
   - Ensure BTCPay Server wallet is secure
   - Use proper backup procedures
   - Monitor wallet balances
   - Set up alerts for unusual activity

2. **Transaction Security**
   - Verify transaction amounts before sending
   - Double-check Bitcoin addresses
   - Monitor for unauthorized transactions
   - Keep transaction records

### Best Security Practices

1. **Regular Updates**
   - Keep BTCPay Server updated
   - Update plugin when new versions available
   - Monitor security advisories

2. **Monitoring**
   - Monitor logs for suspicious activity
   - Set up alerts for failures
   - Review reward history regularly
   - Check for unauthorized access

3. **Backup**
   - Regularly backup BTCPay Server data
   - Backup wallet seed phrases securely
   - Test backup restoration procedures

---

## Conclusion

The Bitcoin Rewards Plugin provides a powerful way to reward customers with Bitcoin automatically. By following this manual, you should be able to:

- Install and configure the plugin
- Set up webhooks with Shopify
- Configure email notifications
- Monitor and manage rewards
- Troubleshoot common issues

For additional support, please refer to:
- BTCPay Server documentation
- Plugin repository (if open source)
- BTCPay Server community forums

**Happy rewarding! 🎉**

---

*Last Updated: November 2025*
*Plugin Version: 1.0.0*
*BTCPay Server Compatibility: 2.0.0+*



