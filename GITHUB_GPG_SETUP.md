# GitHub GPG Key Setup Instructions

Your GPG key has been configured for Git! Here's what you need to do next:

## Your GPG Key Information

- **Key ID**: `87B7DD716DD00BA1`
- **Full Fingerprint**: `E2C5B19EA31BD961ECA3930387B7DD716DD00BA1`
- **Key Type**: RSA 4096
- **Created**: 2025-11-24

## Step 1: Export Your Public Key

Open a new PowerShell window (or restart your current one) and run:

```powershell
gpg --armor --export 87B7DD716DD00BA1
```

If GPG is not found, use the full path:

```powershell
"C:\Program Files (x86)\GnuPG\bin\gpg.exe" --armor --export 87B7DD716DD00BA1
```

Or if installed in a different location:

```powershell
"C:\Program Files\GnuPG\bin\gpg.exe" --armor --export 87B7DD716DD00BA1
```

This will output your public key starting with:
```
-----BEGIN PGP PUBLIC KEY BLOCK-----
...
-----END PGP PUBLIC KEY BLOCK-----
```

**Copy the entire output**, including the BEGIN and END lines.

## Step 2: Add GPG Key to GitHub

1. Go to GitHub: https://github.com/settings/keys
2. Click the **"New GPG key"** button
3. Paste your public key (the entire output from Step 1)
4. Click **"Add GPG key"**
5. Confirm with your GitHub password if prompted

## Step 3: Verify Git Configuration

Your Git is already configured with your signing key! You can verify:

```powershell
git config --global user.signingkey
```

Should output: `87B7DD716DD00BA1`

## Step 4: Enable GPG Signing for All Commits (Optional)

To automatically sign all commits:

```powershell
git config --global commit.gpgsign true
```

Or sign commits individually by using `-S` flag:

```powershell
git commit -S -m "Your commit message"
```

## Step 5: Test GPG Signing

Test that everything works:

```powershell
git commit --allow-empty -S -m "Test GPG signing"
git log --show-signature -1
```

If successful, you should see "Good signature" in the output.

## Troubleshooting

### If GPG command not found:

1. **Restart PowerShell** - The PATH may need to be refreshed
2. **Use full path**:
   ```powershell
   & "C:\Program Files (x86)\GnuPG\bin\gpg.exe" --armor --export 87B7DD716DD00BA1
   ```
3. **Add to PATH permanently**:
   - Right-click "This PC" → Properties → Advanced system settings
   - Click "Environment Variables"
   - Edit "Path" in User variables
   - Add: `C:\Program Files (x86)\GnuPG\bin`

### If Git can't find GPG:

```powershell
git config --global gpg.program "C:\Program Files (x86)\GnuPG\bin\gpg.exe"
```

### If passphrase prompt appears too often:

You may need to configure a GPG agent. See the full GPG_SETUP_INSTRUCTIONS.md for details.

## Next Steps

After adding your GPG key to GitHub:

1. ✅ All future commits signed with this key will show as "Verified" on GitHub
2. ✅ Your commits will have a green "Verified" badge
3. ✅ People can verify that commits came from you

Your key is now ready to use! 🎉

