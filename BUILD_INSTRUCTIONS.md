# Build Instructions for Bitcoin Rewards Plugin

## Quick Start (recommended)

These steps reflect the current repo layout and avoid the build issues we hit.

1) From repo root, build (BTCPay submodule restore happens automatically):
```
dotnet build Plugins/BTCPayServer.Plugins.BitcoinRewards/BTCPayServer.Plugins.BitcoinRewards.csproj -c Release
```
Output: `Plugins/BTCPayServer.Plugins.BitcoinRewards/bin/Release/net8.0/BTCPayServer.Plugins.BitcoinRewards.dll`

2) Package the plugin by renaming the DLL to `.btcpay` at repo root:
```
cp Plugins/BTCPayServer.Plugins.BitcoinRewards/bin/Release/net8.0/BTCPayServer.Plugins.BitcoinRewards.dll \
   BTCPayServer.Plugins.BitcoinRewards.btcpay
```

3) Install into BTCPay (docker example, container `generated_btcpayserver_1`):
```
docker exec generated_btcpayserver_1 mkdir -p /datadir/plugins
docker cp BTCPayServer.Plugins.BitcoinRewards.btcpay generated_btcpayserver_1:/datadir/plugins/
docker restart generated_btcpayserver_1
```
Then enable the plugin in Server Settings > Plugins if needed.

## Why this works
- `Directory.Build.targets` auto-initializes/restores the BTCPay submodule, so no manual `BTCPayServerPath` or pre-built BTCPay checkout is required.
- The current project does **not** emit a `.btcpay` file automatically; you must copy/rename the DLL (step 2).

## Notes on helper scripts
- `scripts/build-local.sh` / `.ps1` currently assume the csproj sits at repo root and will fail here. Use the `dotnet build` command above until the scripts are updated.

## Post-install configuration (high level)
- Square: set Application ID, Access Token, Location ID, environment, and webhook signature key (used to verify incoming webhooks).
- Rates: plugin fetches BTC/fiat via CoinGecko; ensure outbound HTTPS.
- Migrations: plugin migrations auto-apply on startup via `BitcoinRewardsMigrationRunner`.

## Troubleshooting
- Missing BTCPay namespaces/types: ensure you ran the `dotnet build` command from repo root so the submodule restore ran.
- Plugin not showing in BTCPay: confirm the `.btcpay` file exists in `/datadir/plugins` (or your BTCPay plugins directory) and restart BTCPay.

