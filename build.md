# Build Bitcoin Rewards Plugin (.btcpay)

Reliable steps to produce the distributable `.btcpay` file from this repo.

## Prerequisites
- .NET SDK 8.0.x
- Git (for automatic BTCPay submodule fetch)

## Recommended local build (works in this repo)
From the repo root, run:
```
dotnet build Plugins/BTCPayServer.Plugins.BitcoinRewards/BTCPayServer.Plugins.BitcoinRewards.csproj -c Release
```
- This initializes/restores the BTCPay submodule automatically (see `Directory.Build.targets`), so you do NOT need to pre-clone BTCPay or set `BTCPAYSERVER_PATH`.
- Output: `Plugins/BTCPayServer.Plugins.BitcoinRewards/bin/Release/net8.0/BTCPayServer.Plugins.BitcoinRewards.dll`

Package the plugin (rename the DLL to .btcpay) and place it at repo root:
```
cp Plugins/BTCPayServer.Plugins.BitcoinRewards/bin/Release/net8.0/BTCPayServer.Plugins.BitcoinRewards.dll \
   BTCPayServer.Plugins.BitcoinRewards.btcpay
```

## Optional: install into a local BTCPay docker container
Example for the running container named `generated_btcpayserver_1`:
```
docker exec generated_btcpayserver_1 mkdir -p /datadir/plugins
docker cp BTCPayServer.Plugins.BitcoinRewards.btcpay generated_btcpayserver_1:/datadir/plugins/
docker restart generated_btcpayserver_1
```

## Notes on the helper scripts
- `scripts/build-local.sh` and `scripts/build-local.ps1` currently assume the plugin csproj is at repo root, so they will fail as-is. Prefer the `dotnet build` command above until the scripts are updated.

## Verify artifact
```
ls -lh BTCPayServer.Plugins.BitcoinRewards.btcpay
```