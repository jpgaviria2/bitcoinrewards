# GitHub Setup Instructions

Your code has been committed locally. To push to GitHub, follow these steps:

## Option 1: Create a New Repository on GitHub

1. Go to [GitHub](https://github.com) and sign in
2. Click the "+" icon in the top right, then select "New repository"
3. Name your repository (e.g., `btcpayserver-plugin-bitcoinrewards`)
4. **Do NOT** initialize with README, .gitignore, or license (we already have these)
5. Click "Create repository"

## Option 2: Use Existing Repository

If you already have a GitHub repository, use its URL.

## Push Your Code

After creating the repository, run these commands (replace `YOUR_USERNAME` and `YOUR_REPO_NAME`):

```bash
# Add the remote repository
git remote add origin https://github.com/YOUR_USERNAME/YOUR_REPO_NAME.git

# Or if using SSH:
# git remote add origin git@github.com:YOUR_USERNAME/YOUR_REPO_NAME.git

# Push to GitHub
git push -u origin master
```

## If You Need to Change the Remote URL

```bash
# Remove existing remote (if any)
git remote remove origin

# Add new remote
git remote add origin https://github.com/YOUR_USERNAME/YOUR_REPO_NAME.git

# Push
git push -u origin master
```

## Verify

After pushing, verify your code is on GitHub:

```bash
git remote -v
```

You should see your repository URL listed.

## Current Status

✅ Code committed locally (commit: `c8b9a11`)
✅ 31 files committed
✅ Ready to push to GitHub

Just add your remote and push!

