# Bitcoin Rewards for BTCPay Server

Open-source BTCPay Server plugin for issuing Bitcoin/Lightning rewards from verified Square POS payments and BTCPay store activity.

The plugin is designed for physical merchants that want a customer-facing rewards display, optional customer Lightning-address check-in, and admin test tools without embedding store-specific branding or secrets in source code.

## Features

- **Square rewards**: Process verified Square payment webhooks and issue rewards.
- **BTCPay rewards**: Optional rewards for native BTCPay invoices.
- **Lightning pull payments**: Generate BTCPay pull-payment rewards with customer-facing QR display.
- **Scanned Lightning-address flow**: A customer can scan their wallet Lightning-address QR on the kiosk; the next eligible Square order can pay the reward directly to that address.
- **Counter display**: Minimal customer-facing display with configurable logo/colors and waiting/ready/reward states.
- **Admin test center**: Test scanned-address payout, email-flow pull payment, and display-only pull payment paths with a configurable sat amount.
- **Open-source defaults**: No store-specific domains, API keys, branding, or credentials are required or shipped.

## Repository scope

This repository is the BTCPay plugin source.

The Android kiosk app is maintained separately at:

- <https://github.com/jpgaviria2/RewardsApp>

Do not commit Android signing files, Zapstore/Nostr keys, BTCPay/Square credentials, LND macaroons, generated plugin packages, or production deployment overrides to this repository.

## Installation

### Option 1: BTCPay Plugin Builder

1. Open the BTCPay plugin builder.
2. Use this repository URL: `https://github.com/jpgaviria2/bitcoinrewards`.
3. Build/download the `.btcpay` artifact.
4. In BTCPay Server, go to **Server Settings → Plugins → Upload plugin**.
5. Restart BTCPay Server when prompted.

### Option 2: Build locally

```bash
git clone https://github.com/jpgaviria2/bitcoinrewards.git
cd bitcoinrewards
git submodule update --init --recursive

dotnet build Plugins/BTCPayServer.Plugins.BitcoinRewards/BTCPayServer.Plugins.BitcoinRewards.csproj -c Release
```

Package the release output as a `.btcpay` archive. Do not commit generated archives.

## Configuration

1. Go to your BTCPay store.
2. Open **Plugins → Bitcoin Rewards**.
3. Enable the plugin.
4. Select enabled platforms:
   - Square
   - BTCPay
5. Configure reward percentage, maximum reward cap, payout processor, and display settings.
6. Configure Square credentials and webhook signing in BTCPay admin only. Never commit them.

## Customer Lightning-address check-in

When enabled, the kiosk can accept a customer Lightning-address QR. The plugin stores the address as a short-lived pending destination. The next eligible Square order consumes it and sends the configured reward to that Lightning address.

If no pending scan exists, the legacy pull-payment/email/display reward flow remains available.

## Admin test center

The admin test center supports three flows:

1. **Scanned Lightning address test** — arms a one-time test; the next captured Lightning address receives the configured test amount automatically.
2. **Email flow test** — creates a pull-payment reward for a test email address.
3. **Display pull-payment test** — creates a pull-payment reward for the customer display without requiring a Lightning-address scan.

The settings page also shows the latest scanned-address test state: armed/not armed, amount, reward status, direct payout state, BTCPay payout state, transaction/order IDs, payment hash, and error information when present.

## Security expectations

- Webhook handlers must verify provider signatures.
- Admin actions require BTCPay store-management authorization.
- Secrets belong in BTCPay settings, environment variables, or a password manager — not source control.
- `.secrets/`, keystores, generated plugin artifacts, and build outputs are ignored.
- Direct Lightning payout tests move real sats. Use small test amounts and verify the destination before testing.

See [SECURITY.md](SECURITY.md) for vulnerability reporting and operational hardening guidance.

## Build and development docs

- [Build instructions](docs/BUILD_INSTRUCTIONS.md)
- [Plugin compliance](docs/PLUGIN_COMPLIANCE.md)
- [Admin guide](docs/ADMIN-GUIDE.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)

## License

MIT — see [LICENSE](LICENSE).
