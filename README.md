# BTCPayServer Bitcoin Rewards Plugin

Bitcoin-backed rewards for BTCPay Server merchants with Square and BTCPay invoice support. Shopify is temporarily disabled (toggle locked off, "coming soon").

## Features
- Reward creation for BTCPay invoices (with buyer email) and Square payments
- Lightning pull-payments for reward redemption
- Email notifications for reward claims
- Configurable reward percentages, minimums, and caps per platform
- **Physical store display mode**: Real-time QR code display for rewards when email is unavailable (perfect for in-store Square transactions)

## Build
From repo root:
```bash
dotnet build Plugins/BTCPayServer.Plugins.BitcoinRewards/BTCPayServer.Plugins.BitcoinRewards.csproj -c Release
cp Plugins/BTCPayServer.Plugins.BitcoinRewards/bin/Release/net8.0/BTCPayServer.Plugins.BitcoinRewards.dll BTCPayServer.Plugins.BitcoinRewards.btcpay
```

## Install (docker example)
```bash
docker exec generated_btcpayserver_1 mkdir -p /datadir/plugins
docker cp BTCPayServer.Plugins.BitcoinRewards.btcpay generated_btcpayserver_1:/datadir/plugins/
docker restart generated_btcpayserver_1
```
Enable via BTCPay Server Settings → Plugins.

## Configuration Notes
- Set reward percentages and enabled platforms (Shopify locked off)
- Square: configure Application ID, Access Token, Location ID, environment, and webhook signature key (used to verify incoming webhooks)
- BTCPay: rewards require buyer email on invoices
- Email delivery: ensure SMTP is configured in BTCPay
- Rates: plugin fetches BTC/fiat rates via CoinGecko; ensure outbound HTTPS allowed

## Physical Store Display Mode

For physical stores where customer email/phone is not collected (common with Square POS), enable **Display Mode** to broadcast rewards to a nearby display device via SignalR. Customers can scan the QR code with their Lightning wallet to claim rewards immediately.

### Quick Setup
1. Enable "Fallback to Display When No Email" in plugin settings
2. Set up display device with the provided display app (see `display-app/` folder)
3. Configure display with your BTCPay Server URL and Store ID
4. Display automatically shows QR codes when payments are received without email

### Documentation
- **[Physical Store Display Setup Guide](DISPLAY_SETUP_GUIDE.md)** - Complete setup instructions
- **[Display Testing Guide](DISPLAY_TESTING_GUIDE.md)** - Testing procedures
- **[Display App README](display-app/README.md)** - Display application documentation

### Features
- Real-time SignalR broadcasting from plugin to display devices
- Configurable display duration (default: 60 seconds)
- Auto-reconnect on network interruption
- Latest-only display mode (shows most recent reward)
- Fullscreen/kiosk mode support for tablets and monitors

## Development tips
- Repo includes BTCPay Server as a submodule; `Directory.Build.targets` restores/builds dependencies automatically during `dotnet build`.
- For local debug with BTCPay, point `DEBUG_PLUGINS` to the built DLL if needed.

## Repository info
- Target: .NET 8
- Plugin output: `.btcpay` package built from `BTCPayServer.Plugins.BitcoinRewards.dll`
- License: MIT
- Migrations: plugin migrations auto-apply on startup via `BitcoinRewardsMigrationRunner`.
