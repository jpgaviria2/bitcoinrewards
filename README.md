# BTCPayServer Bitcoin Rewards Plugin

Bitcoin-backed rewards for BTCPay Server merchants with Square and BTCPay invoice support. Shopify is temporarily disabled (toggle locked off, “coming soon”).

## Features
- Reward creation for BTCPay invoices (with buyer email) and Square payments.
- Lightning pull-payments for reward redemption.
- Email notifications for reward claims.
- Configurable reward percentages, minimums, and caps per platform.

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
- Set reward percentages and enabled platforms (Shopify locked off).
- Square: configure Application ID, Access Token, Location ID, environment.
- BTCPay: rewards require buyer email on invoices.
- Email delivery: ensure SMTP is configured in BTCPay.

## Development tips
- Repo includes BTCPay Server as a submodule; `Directory.Build.targets` restores/builds dependencies automatically during `dotnet build`.
- For local debug with BTCPay, point `DEBUG_PLUGINS` to the built DLL if needed.

## Repository info
- Target: .NET 8
- Plugin output: `.btcpay` package built from `BTCPayServer.Plugins.BitcoinRewards.dll`
- License: MIT
