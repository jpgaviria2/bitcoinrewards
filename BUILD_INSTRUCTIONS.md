# Build Instructions for Bitcoin Rewards Plugin

## Quick Start

This plugin is designed to be built and distributed via the [BTCPay Server Plugin Builder](https://plugin-builder.btcpayserver.org/).

## Local Development Build

For local development and testing:

### Prerequisites

1. .NET 8.0 SDK installed
2. BTCPay Server source code cloned (for DLL references)
   - Or a BTCPay Server installation with DLLs accessible

### Build Steps

1. **Set BTCPay Server Path** (choose one method):

   **Option A: Environment Variable**
   ```powershell
   $env:BTCPayServerPath = "C:\path\to\btcpayserver\BTCPayServer\bin\Release\net8.0"
   ```

   **Option B: Update .csproj**
   - Edit `BTCPayServer.Plugins.BitcoinRewards.csproj`
   - Update the default `BTCPayServerLibPath` in the PropertyGroup

2. **Build the Plugin**:
   ```powershell
   cd BTCPayServer.Plugins.BitcoinRewards
   dotnet build -c Release
   ```

3. **Output**:
   - The `.btcpay` file will be in `bin/Release/net8.0/`
   - File name: `BTCPayServer.Plugins.BitcoinRewards.btcpay`

### Build Output

The build process:
1. Compiles the plugin code
2. Embeds all `.cshtml` view files as resources
3. References BTCPay Server DLLs (but doesn't bundle them - `Private="False"`)
4. Renames the output from `.dll` to `.btcpay` (Release builds only)

## Plugin Builder Build

The Plugin Builder will automatically:
1. Clone the repository
2. Provide BTCPay Server DLLs during build
3. Build the plugin
4. Generate the `.btcpay` file
5. Publish it for distribution

### Plugin Builder Configuration

When registering with Plugin Builder:
- **Repository URL**: `https://github.com/yourusername/btcpayserver-plugin-bitcoinrewards`
- **Plugin Directory**: `BTCPayServer.Plugins.BitcoinRewards`
- **Git Ref**: `master` (or specific version tag)

## Installation

Once you have the `.btcpay` file:

1. **Copy to Plugins Directory**:
   - Docker: `/btcpay/plugins/BTCPayServer.Plugins.BitcoinRewards.btcpay`
   - Manual: `{BTCPayServerDirectory}/Plugins/BTCPayServer.Plugins.BitcoinRewards.btcpay`

2. **Restart BTCPay Server**

3. **Enable Plugin**:
   - Go to Server Settings > Plugins
   - Find "Bitcoin Rewards" and enable it

## Troubleshooting

### Build Errors: DLL Not Found

If you get errors about missing BTCPay Server DLLs:
- Ensure BTCPay Server is built first
- Check that `BTCPayServerPath` points to the correct directory
- Verify DLLs exist in the specified path

### Plugin Not Loading

If the plugin doesn't appear in BTCPay Server:
- Check file extension is `.btcpay` (not `.dll`)
- Verify file is in the correct plugins directory
- Check BTCPay Server logs for errors
- Ensure BTCPay Server version is compatible (2.0.0+)

## Notes

- The plugin does NOT bundle BTCPay Server DLLs
- DLLs are provided by BTCPay Server at runtime
- This ensures compatibility across different BTCPay Server installations
- The `.btcpay` file contains only the plugin code and embedded resources

