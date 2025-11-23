# Plugin Builder Configuration

## Repository Information

- **Repository URL**: `https://github.com/jpgaviria2/bitcoinrewards.git`
- **Plugin Directory**: `Plugins/BTCPayServer.Plugins.BitcoinRewards`
- **Git Ref/Branch**: `master`

## Important Notes

⚠️ **Make sure to use the full path including `Plugins/` prefix when configuring the plugin directory in Plugin Builder.**

The `.csproj` file is located at:
```
Plugins/BTCPayServer.Plugins.BitcoinRewards/BTCPayServer.Plugins.BitcoinRewards.csproj
```

## Verification

After updating the Plugin Builder configuration, the build should:
1. Find the `.csproj` file at the specified plugin directory path
2. Build the plugin successfully
3. Generate the `.btcpay` file

If you still see validation errors, ensure:
- The plugin directory path is exactly: `Plugins/BTCPayServer.Plugins.BitcoinRewards`
- No trailing slashes
- Case-sensitive (use exact casing)

