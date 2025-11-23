# Setup Verification Checklist

## ✅ Repository Structure Verified

The repository structure is now correctly aligned with the Shopify plugin pattern:

```
btcpayserver-plugin-bitcoinrewards/
├── Plugins/
│   ├── BTCPayServer.Plugins.BitcoinRewards/
│   │   └── BTCPayServer.Plugins.BitcoinRewards.csproj ✅
│   └── BTCPayServer.Plugins.BitcoinRewards.Tests/
├── submodules/
│   └── (BTCPay Server source) ✅
├── .gitmodules ✅
├── Directory.Build.props ✅
├── Directory.Build.targets ✅
└── README.md ✅
```

## ✅ All Changes Completed

1. **Square Functionality Removed** ✅
   - SquareApiService.cs deleted
   - All Square references removed from code
   - All Square references removed from documentation

2. **Repository Restructured** ✅
   - Plugin code moved to `Plugins/BTCPayServer.Plugins.BitcoinRewards/`
   - Tests moved to `Plugins/BTCPayServer.Plugins.BitcoinRewards.Tests/`
   - `btcpayserver/` renamed to `submodules/`
   - All path references updated

3. **Configuration Files Updated** ✅
   - `.csproj` paths updated to use `../../submodules/`
   - `Directory.Build.targets` updated to reference `submodules/`
   - `.gitmodules` updated with correct path

4. **Documentation Updated** ✅
   - README.md updated with new structure
   - BUILD_INSTRUCTIONS.md updated
   - PLUGIN_BUILDER_CONFIG.md created

## 🔧 Plugin Builder Configuration Required

**IMPORTANT:** You must configure the Plugin Builder with the following settings:

### Required Settings

- **Repository URL**: `https://github.com/jpgaviria2/bitcoinrewards.git`
- **Plugin Directory**: `Plugins/BTCPayServer.Plugins.BitcoinRewards` ⚠️ **Include `Plugins/` prefix!**
- **Git Ref/Branch**: `master`

### Why This Matters

The Plugin Builder error "No .csproj found in '/' at HEAD" occurs because:
1. The Plugin Builder first checks the root directory (`/`)
2. Our `.csproj` is located at `Plugins/BTCPayServer.Plugins.BitcoinRewards/`
3. The Plugin Builder needs to know the correct plugin directory path

### Solution

When configuring the plugin in Plugin Builder:
- **Do NOT use**: `BTCPayServer.Plugins.BitcoinRewards` ❌
- **DO use**: `Plugins/BTCPayServer.Plugins.BitcoinRewards` ✅

The full path must include the `Plugins/` directory prefix.

## 📍 File Locations

- **.csproj file**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/BTCPayServer.Plugins.BitcoinRewards.csproj`
- **Main plugin class**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/BitcoinRewardsPlugin.cs`
- **Submodules**: `submodules/`
- **Solution file**: `submodules/btcpayserver.sln`

## ✅ Verification Steps

1. ✅ Repository structure matches Shopify plugin pattern
2. ✅ All Square functionality removed
3. ✅ All paths updated correctly
4. ✅ Documentation updated
5. ✅ All changes pushed to GitHub
6. ⚠️ **Plugin Builder configuration needs to be updated**

## 🚀 Next Steps

1. **Update Plugin Builder Configuration**:
   - Log into Plugin Builder
   - Edit your plugin settings
   - Set Plugin Directory to: `Plugins/BTCPayServer.Plugins.BitcoinRewards`
   - Save settings

2. **Trigger Build**:
   - Build should now find the `.csproj` file
   - Validation should pass
   - Plugin should build successfully

3. **Verify Build Output**:
   - Check that `.btcpay` file is generated
   - Verify no errors in build logs

## 📝 Notes

- The repository is ready for building
- All code changes are complete
- Only Plugin Builder configuration needs to be updated
- Once configured correctly, builds should work automatically

