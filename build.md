# Build Bitcoin Rewards Plugin (.btcpay) for testing

Follow these steps to produce the distributable `.btcpay` plugin file at the repository root.

## Prerequisites
- .NET SDK 8.0.x
- Git (only if BTCPay Server needs to be cloned for dependencies)
- PowerShell 7+ (Windows/macOS/Linux) or Bash (Linux/macOS/WSL)

## Quick build (Windows / PowerShell)
1) From the repo root, run:
```
pwsh -File scripts/build-local.ps1
```
   - By default this will clone and build BTCPay Server next to the repo (../btcpayserver).  
   - To reuse an existing BTCPay build, pass `-SkipBTCPayServerBuild` and set `-BTCPayServerPath` to the directory containing `BTCPayServer.dll` (Release/net8.0).

2) After a successful build, copy the artifact to the repo root:
```
Copy-Item bin\Release\net8.0\BTCPayServer.Plugins.BitcoinRewards.btcpay .\
```

## Quick build (Linux/macOS/WSL / Bash)
1) From the repo root, run:
```
./scripts/build-local.sh
```
   - Uses `BTCPAYSERVER_PATH` if you already have built BTCPay Server; otherwise it will clone and build it in `../btcpayserver`.
   - Set `SKIP_BTCPAYSERVER_BUILD=true` to skip cloning/building BTCPay if you provide `BTCPAYSERVER_PATH` (path that contains `BTCPayServer.dll` under Release/net8.0).

2) Copy the artifact to the repo root:
```
cp bin/Release/net8.0/BTCPayServer.Plugins.BitcoinRewards.btcpay ./
```

## Verifying the build
- Ensure the file exists at `./BTCPayServer.Plugins.BitcoinRewards.btcpay`.
- Optionally check size and timestamp:
  - PowerShell: `Get-Item .\BTCPayServer.Plugins.BitcoinRewards.btcpay | Format-List Name,Length,LastWriteTime`
  - Bash: `ls -lh BTCPayServer.Plugins.BitcoinRewards.btcpay`

You can now upload or install this `.btcpay` file on a BTCPay Server instance for testing. 



