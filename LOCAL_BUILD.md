# Local Build Instructions for Bitcoin Rewards Plugin

This guide explains how to build the Bitcoin Rewards plugin locally, replicating the Plugin Builder environment for troubleshooting.

## Prerequisites

1. **.NET 8.0 SDK** (version 8.0.416 or later)
   - Download from: https://dotnet.microsoft.com/download/dotnet/8.0
   - Verify installation: `dotnet --version`

2. **BTCPay Server Source Code** (for DLL references)
   - Clone: `git clone https://github.com/btcpayserver/btcpayserver.git`
   - Build BTCPay Server: `cd btcpayserver && dotnet build BTCPayServer/BTCPayServer.csproj -c Release`

3. **Git** (for cloning repositories)

## Quick Start

### Option 1: Using Build Scripts (Recommended)

#### Windows (PowerShell)
```powershell
.\scripts\build-local.ps1
```

#### Linux/Mac (Bash)
```bash
./scripts/build-local.sh
```

The script will:
1. Automatically clone and build BTCPay Server if needed
2. Set the `BTCPayServerPath` environment variable
3. Build the plugin
4. Generate the `.btcpay` file

### Option 2: Manual Build

1. **Set BTCPay Server Path**:
   ```powershell
   # Windows
   $env:BTCPayServerPath = "C:\path\to\btcpayserver\BTCPayServer\bin\Release\net8.0"
   
   # Linux/Mac
   export BTCPayServerPath="/path/to/btcpayserver/BTCPayServer/bin/Release/net8.0"
   ```

2. **Build the Plugin**:
   ```bash
   dotnet build BTCPayServer.Plugins.BitcoinRewards.csproj -c Release
   ```

3. **Output**:
   - The `.btcpay` file will be in `bin/Release/net8.0/`
   - File name: `BTCPayServer.Plugins.BitcoinRewards.btcpay`

## Build Script Options

### PowerShell Script (`build-local.ps1`)

```powershell
# Build with default settings
.\scripts\build-local.ps1

# Skip BTCPay Server build (use existing)
.\scripts\build-local.ps1 -SkipBTCPayServerBuild

# Specify custom BTCPay Server path
.\scripts\build-local.ps1 -BTCPayServerPath "C:\custom\path"

# Clean build
.\scripts\build-local.ps1 -Clean
```

### Bash Script (`build-local.sh`)

```bash
# Build with default settings
./scripts/build-local.sh

# Skip BTCPay Server build
SKIP_BTCPAYSERVER_BUILD=true ./scripts/build-local.sh

# Specify custom BTCPay Server path
BTCPAYSERVER_PATH="/custom/path" ./scripts/build-local.sh

# Clean build
CLEAN=true ./scripts/build-local.sh
```

## Docker Build (Exact Plugin Builder Environment)

To replicate the exact Plugin Builder environment (Debian 12, .NET 8.0.416):

1. **Build Docker Image**:
   ```bash
   cd docker
   ./docker-build.sh
   ```

2. **Run Build in Docker**:
   ```bash
   docker run --rm -v "$(pwd):/build" -w /build btcpayserver-plugin-bitcoinrewards-build /build/scripts/build-local.sh
   ```

## Troubleshooting

### Error: BTCPay Server DLLs Not Found

**Solution**: Ensure BTCPay Server is built and the path is correct:
```powershell
# Verify DLLs exist
Test-Path "$env:BTCPayServerPath\BTCPayServer.dll"

# Rebuild BTCPay Server if needed
cd ..\btcpayserver
dotnet build BTCPayServer\BTCPayServer.csproj -c Release
```

### Error: Missing Extension Methods

**Solution**: The `_ViewImports.cshtml` file should include:
```razor
@using BTCPayServer
@using BTCPayServer.Abstractions.Extensions
@using BTCPayServer.Data
```

### Error: Ambiguous Method Calls

**Solution**: Use fully qualified names in Razor views:
```razor
@BTCPayServer.Plugins.BitcoinRewards.BitcoinRewardsExtensions.GetBitcoinRewardsSettings(store.GetStoreBlob())
```

### Error: EventAggregator Not Found

**Solution**: `EventAggregator` is in `BTCPayServer` namespace, not `BTCPayServer.HostedServices`:
```csharp
using BTCPayServer; // Correct
// NOT: using BTCPayServer.HostedServices;
```

## Build Output

### Successful Build

```
=== Build SUCCESS ===
Plugin built successfully: bin\Release\net8.0\BTCPayServer.Plugins.BitcoinRewards.btcpay
File size: XXXXX bytes
Last modified: [timestamp]
```

### Build Artifacts

- **`.btcpay` file**: The plugin binary (renamed from `.dll`)
- **`.pdb` file**: Debug symbols (if available)
- **Location**: `bin/Release/net8.0/`

## Project Structure

```
btcpayserver-plugin-bitcoinrewards/
├── BTCPayServer.Plugins.BitcoinRewards.csproj  # Main project file
├── scripts/
│   ├── build-local.ps1                          # Windows build script
│   ├── build-local.sh                           # Linux/Mac build script
│   ├── build-btcpayserver.ps1                  # BTCPay Server build (Windows)
│   └── build-btcpayserver.sh                    # BTCPay Server build (Linux/Mac)
├── docker/
│   ├── Dockerfile.build                         # Docker build environment
│   └── docker-build.sh                          # Docker build script
└── bin/Release/net8.0/
    └── BTCPayServer.Plugins.BitcoinRewards.btcpay  # Output file
```

## DLL References

The plugin references the following BTCPay Server DLLs (not bundled):

- `BTCPayServer.dll`
- `BTCPayServer.Abstractions.dll`
- `BTCPayServer.Data.dll`
- `BTCPayServer.Client.dll`
- `BTCPayServer.Common.dll`
- `BTCPayServer.Rating.dll`

These are provided by BTCPay Server at runtime and must be available during build.

## Next Steps

After a successful build:

1. **Test Locally**: Copy the `.btcpay` file to your BTCPay Server plugins directory
2. **Upload to Plugin Builder**: Use the Plugin Builder website to build and distribute
3. **Manual Distribution**: Share the `.btcpay` file directly

## Additional Resources

- [BTCPay Server Plugin Documentation](https://docs.btcpayserver.org/Development/Plugins/)
- [Plugin Builder](https://plugin-builder.btcpayserver.org/)
- [.NET 8.0 Documentation](https://docs.microsoft.com/dotnet/core/)

