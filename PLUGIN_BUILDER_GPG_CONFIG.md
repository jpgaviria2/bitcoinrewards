# Plugin Builder GPG/PGP Configuration

## Understanding GPG Signing in Plugin Builder Context

There are **two different types of GPG signing** that might be relevant:

1. **Git Commit Signing** (what you've configured) ✅
2. **Plugin Package Signing** (what Plugin Builder does)

## Current Situation

From your build log, I can see:
- ✅ Plugin builds successfully
- ⚠️ Warning: `bash: line 1: gpg: command not found`
- ✅ Plugin package created: `BTCPayServer.Plugins.BitcoinRewards.btcpay`

The "gpg: command not found" warning is **harmless** - your plugin still builds and works correctly. It just means the package wasn't signed during the build process.

## Why Your Local GPG Key Isn't Used by Plugin Builder

The **Plugin Builder is a remote service** that:
- Runs on a Linux build server (Debian 12 in your case)
- Clones your repository in an isolated environment
- Has its own build tools and dependencies
- **Does not have access to your local GPG keys**

Your GPG key (`87B7DD716DD00BA1`) is:
- ✅ Configured on your **local Windows machine**
- ✅ For signing **Git commits** (which is already set up)
- ❌ **Not available** to the Plugin Builder's Linux build environment

## Plugin Package Signing

The Plugin Builder attempts to sign `.btcpay` packages, but:

1. **GPG is not installed** in the Plugin Builder's build environment
2. **Plugin Builder typically uses its own signing keys** (if any), not repository owner keys
3. **Unsigned packages still work** - BTCPay Server accepts unsigned plugins

## Options for Plugin Package Signing

### Option 1: Accept Unsigned Packages (Current State) ✅

**Pros:**
- ✅ Works immediately - no configuration needed
- ✅ Plugin Builder builds successfully
- ✅ Plugins install and work in BTCPay Server
- ✅ Most users don't verify package signatures anyway

**Cons:**
- ⚠️ No cryptographic verification that the package came from you
- ⚠️ No protection against package tampering

**Recommendation:** This is fine for most use cases. The Plugin Builder is a trusted service, and your code is in a public Git repository.

### Option 2: Sign Packages Locally (After Download)

If you want signed packages:

1. **Download the `.btcpay` file** from Plugin Builder
2. **Sign it locally** using your GPG key:
   ```powershell
   gpg --armor --detach-sig --output BTCPayServer.Plugins.BitcoinRewards.btcpay.asc BTCPayServer.Plugins.BitcoinRewards.btcpay
   ```
3. **Distribute both files**:
   - `BTCPayServer.Plugins.BitcoinRewards.btcpay` (plugin)
   - `BTCPayServer.Plugins.BitcoinRewards.btcpay.asc` (signature)

4. **Users can verify**:
   ```bash
   gpg --verify BTCPayServer.Plugins.BitcoinRewards.btcpay.asc BTCPayServer.Plugins.BitcoinRewards.btcpay
   ```

### Option 3: Contact Plugin Builder Maintainers

If you want Plugin Builder to sign packages on your behalf:

1. **Contact BTCPay Server team** about Plugin Builder signing options
2. **Provide your public GPG key** if they support per-plugin signing
3. **Coordinate key management** (they'd need access to configure signing)

This is typically not supported, as Plugin Builder is designed to be a simple build service.

## What You've Already Configured ✅

Your GPG setup is correct for **Git commit signing**:

- ✅ GPG key generated: `87B7DD716DD00BA1`
- ✅ Git configured to use it: `git config --global user.signingkey 87B7DD716DD00BA1`
- ✅ Auto-sign commits enabled: `git config --global commit.gpgsign true`

**Next step:** Add your public key to GitHub (see `GITHUB_GPG_SETUP.md`)

This will make your **Git commits** show as "Verified" on GitHub, which is what most people care about.

## Summary

| Type | Purpose | Configured? | Used By |
|------|---------|-------------|---------|
| Git Commit Signing | Verify commits on GitHub | ✅ Yes | You (locally) |
| Plugin Package Signing | Verify `.btcpay` files | ❌ No | Plugin Builder (not configured) |

**Recommendation:**
- ✅ **Keep Git commit signing** - This is working and valuable
- ✅ **Accept unsigned plugin packages** - The warning is harmless
- 📝 **Add your GPG key to GitHub** - So commits show as verified

The Plugin Builder's GPG warning doesn't affect functionality. Your plugin builds, packages, and installs correctly!

## FAQ

**Q: Can I configure Plugin Builder to use my GPG key?**  
A: No, Plugin Builder runs in its own isolated environment without access to your keys.

**Q: Is the unsigned package a security issue?**  
A: Not really - your code is in a public Git repo, and Plugin Builder is a trusted service. Users can verify by checking the Git commit signatures.

**Q: Should I be concerned about the warning?**  
A: No, it's informational. The plugin builds successfully regardless.

**Q: Can users verify the plugin comes from me?**  
A: Yes, through Git commit signatures on GitHub. Package signing is less critical since the source is public.

