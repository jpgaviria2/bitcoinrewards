# GPG Key Generation Instructions for Windows

This guide will help you install GPG and generate a key pair for signing Git commits and plugin packages.

## Step 1: Install Gpg4win

1. **Download Gpg4win**:
   - Visit: https://www.gpg4win.org/download.html
   - Download the latest version (typically `gpg4win-x.x.x.exe`)

2. **Install Gpg4win**:
   - Run the installer
   - During installation, ensure "GnuPG" is selected (it should be by default)
   - Complete the installation wizard

3. **Verify Installation**:
   - Open PowerShell (you may need to restart it)
   - Run: `gpg --version`
   - You should see version information

## Step 2: Generate Your GPG Key

### Option A: Using Command Line (Recommended)

1. **Start GPG Key Generation**:
   ```powershell
   gpg --full-generate-key
   ```

2. **Select Key Type**:
   - Press `Enter` to accept the default (RSA and RSA)

3. **Set Key Size**:
   - Type `4096` and press `Enter` (recommended for security)

4. **Set Expiration**:
   - Type `0` and press `Enter` for key that doesn't expire
   - Or set a specific duration (e.g., `2y` for 2 years)

5. **Confirm Settings**:
   - Type `y` and press `Enter`

6. **Enter Your Information**:
   - **Real name**: Your full name (e.g., `Juan Pablo Gaviria`)
   - **Email address**: Your email (e.g., `jpgaviria2@example.com`)
   - **Comment**: Optional, can leave blank

7. **Review and Confirm**:
   - Type `O` (for "Okay") and press `Enter`

8. **Set Passphrase**:
   - Enter a strong passphrase (you'll need this to use the key)
   - Confirm the passphrase

### Option B: Using Kleopatra (GUI)

1. **Open Kleopatra** (installed with Gpg4win)
2. **Click "New Key Pair"**
3. **Fill in your information**:
   - Name
   - Email
   - Optional: Comment
4. **Click "Create"** and follow the wizard
5. **Set a passphrase** when prompted

## Step 3: List Your Keys

After generating your key, verify it was created:

```powershell
gpg --list-secret-keys --keyid-format=long
```

You'll see output like:
```
sec   rsa4096/1234567890ABCDEF 2024-01-01 [SC]
      ABCDEF1234567890ABCDEF1234567890ABCDEF12
uid                 [ultimate] Your Name <your.email@example.com>
ssb   rsa4096/FEDCBA0987654321 2024-01-01 [E]
```

The part after `rsa4096/` is your **Key ID** (e.g., `1234567890ABCDEF`).

## Step 4: Export Your Public Key

Export your public key to share with others (like adding to GitHub):

```powershell
gpg --armor --export YOUR_KEY_ID
```

Replace `YOUR_KEY_ID` with your actual key ID from Step 3. This will print your public key to the console.

To save it to a file:
```powershell
gpg --armor --export YOUR_KEY_ID > public-key.asc
```

## Step 5: Configure Git to Use GPG Signing

1. **Tell Git about your GPG key**:
   ```powershell
   git config --global user.signingkey YOUR_KEY_ID
   ```

2. **Enable GPG signing for all commits** (optional):
   ```powershell
   git config --global commit.gpgsign true
   ```

   Or sign commits individually:
   ```powershell
   git commit -S -m "Your commit message"
   ```

## Step 6: Add GPG Key to GitHub

1. **Export your public key**:
   ```powershell
   gpg --armor --export YOUR_KEY_ID
   ```
   Copy the entire output (including `-----BEGIN PGP PUBLIC KEY BLOCK-----` and `-----END PGP PUBLIC KEY BLOCK-----`)

2. **Add to GitHub**:
   - Go to: https://github.com/settings/keys
   - Click "New GPG key"
   - Paste your public key
   - Click "Add GPG key"

## Step 7: Test GPG Signing

Test that everything works:

```powershell
git commit --allow-empty -S -m "Test GPG signing"
```

If successful, you should see something like:
```
[master (root-commit) abc1234] Test GPG signing
```

## Troubleshooting

### GPG command not found after installation
- Restart your PowerShell/terminal
- Verify installation path: `C:\Program Files (x86)\GnuPG\bin\gpg.exe`
- Add to PATH if needed

### Git can't find GPG
```powershell
git config --global gpg.program "C:\Program Files (x86)\GnuPG\bin\gpg.exe"
```

### For Windows Subsystem for Linux (WSL)
If you're using WSL, install GPG there:
```bash
sudo apt-get update
sudo apt-get install gnupg
```

## For Plugin Builder

The Plugin Builder uses GPG to sign `.btcpay` packages. Having a GPG key configured will allow your plugins to be signed automatically during the build process.

The warning you saw (`bash: line 1: gpg: command not found`) indicates GPG isn't available in the Plugin Builder environment, but this doesn't prevent the plugin from being created - it just won't be signed.

