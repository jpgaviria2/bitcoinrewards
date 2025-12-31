# Bitcoin Rewards Plugin for BTCPay Server

Automatically reward your customers with Bitcoin! This BTCPay Server plugin integrates with Square POS and native BTCPay invoices to issue Lightning rewards via pull payments.

## Features

- **Multi-Platform Support**: Square POS and BTCPay native invoices
- **Lightning Pull Payments**: Instant LNURL-based reward claims
- **Customizable Display Page**: Built-in QR code display with HTML template editor
- **Email Notifications**: Automatic reward emails with claim links
- **Flexible Configuration**: Set reward percentages per platform, minimum amounts, and caps
- **Automatic Payouts**: Integrate with BTCPay payout processors for automated reward distribution
- **Physical Store Ready**: Display QR codes in-store for instant mobile wallet claims

## Installation

### Using BTCPay Plugin Builder (Recommended)

1. Visit [BTCPay Plugin Builder](https://github.com/btcpayserver/btcpayserver-plugin-builder)
2. Point to this repository: `https://github.com/jpgaviria2/bitcoinrewards`
3. Download the generated `.btcpay` file
4. Upload via BTCPay Server → Settings → Plugins → Upload Plugin

### Manual Installation (Docker)

```bash
# Copy plugin to BTCPay container
docker exec generated_btcpayserver_1 mkdir -p /datadir/plugins
docker cp BTCPayServer.Plugins.BitcoinRewards.btcpay generated_btcpayserver_1:/datadir/plugins/
docker restart generated_btcpayserver_1
```

Enable via BTCPay Server Settings → Plugins.

## Configuration

### Basic Setup

1. Navigate to your store → **Plugins → Bitcoin Rewards**
2. Enable the plugin
3. Set reward percentages:
   - **Square/Shopify**: Percentage for external platform payments
   - **BTCPay**: Percentage for direct BTCPay invoice payments
4. Configure minimum transaction amount (optional)
5. Set maximum reward cap in satoshis (optional)

### Square Integration

Required fields:
- Application ID
- Access Token
- Location ID
- Environment (production/sandbox)
- Webhook Signature Key

### BTCPay Invoice Rewards

- Rewards are automatically created when invoices are paid
- Works with or without buyer email
- Email rewards sent if email provided
- Display mode shows QR for walk-in customers

### Payout Processors

Configure a payout processor in **Store Settings → Payout Processors** to enable automated reward payments. The plugin creates pull payments that can be automatically processed.

### Display Page

Access your rewards display at:
```
https://your-btcpay-server/stores/{storeId}/plugins/bitcoin-rewards/display
```

**Customization:**
- Edit HTML template in plugin settings
- Use tokens: `{AMOUNT_SATS}`, `{QR_CODE}`, `{COUNTDOWN_TIMER}`, `{DONE_BUTTON}`
- Live preview available in settings
- Perfect for tablets, kiosks, or customer-facing displays

### Email Templates

Customize reward notification emails:
- Subject line template
- HTML email body with tokens
- Preview before saving

## Display Page Tokens

Available tokens for custom templates:

| Token | Description |
|-------|-------------|
| `{AMOUNT_BTC}` | Reward amount in BTC (e.g., 0.00123456) |
| `{AMOUNT_SATS}` | Reward amount in satoshis (e.g., 123,456) |
| `{QR_CODE}` | LNURL QR code image |
| `{CLAIM_LINK}` | Full HTTPS claim URL |
| `{LNURL}` | LNURL Bech32 string |
| `{COUNTDOWN_TIMER}` | Auto-hiding countdown timer |
| `{DONE_BUTTON}` | Manual dismiss button |

## Building from Source

See [docs/BUILD_INSTRUCTIONS.md](docs/BUILD_INSTRUCTIONS.md) for detailed build instructions.

Quick build:
```bash
dotnet publish Plugins/BTCPayServer.Plugins.BitcoinRewards --configuration Release --output /tmp/plugin-build
cd /tmp/plugin-build && zip -r BTCPayServer.Plugins.BitcoinRewards.btcpay .
```

## Requirements

- BTCPay Server >= 2.3.0
- .NET 8.0
- SMTP configured (for email notifications)
- Outbound HTTPS access (for BTC rate fetching via CoinGecko)

## Documentation

- [Build Instructions](docs/BUILD_INSTRUCTIONS.md)
- [Plugin Compliance](docs/PLUGIN_COMPLIANCE.md)
- [Roadmap](docs/ROADMAP.md)

## Support & Contributions

- **Issues**: [GitHub Issues](https://github.com/jpgaviria2/bitcoinrewards/issues)
- **Source**: [GitHub Repository](https://github.com/jpgaviria2/bitcoinrewards)

## License

MIT License - see [LICENSE](LICENSE) file for details.
