# Cashu Wallet Top-Up and Plugin Conflicts

## Current Architecture

The Bitcoin Rewards plugin uses an **internal Cashu wallet** (`InternalCashuWallet`) that:
- Uses DotNut library directly (no reflection for wallet operations)
- **Optionally** uses Cashu plugin's database for storing/retrieving proofs (via reflection)
- Can work independently if Cashu plugin is not installed (but can't persist proofs without it)

## How to Top Up the Cashu Wallet

### Method 1: Automatic Top-Up via Lightning (NUT-04)

When you create a reward and there's insufficient ecash balance:

1. **Check Ecash Balance**: Plugin checks stored proofs in database
2. **Check Lightning Balance**: If ecash is insufficient, checks Lightning wallet balance
3. **Mint from Lightning**: If Lightning has sufficient balance:
   - Creates a mint quote from the Cashu mint
   - Pays the Lightning invoice automatically
   - Receives new proofs from the mint
   - Stores proofs in database
4. **Create Token**: Combines existing + newly minted proofs into a Cashu token
5. **Send Email**: Sends the token to the customer

**This happens automatically** - no manual intervention needed.

### Method 2: Manual Top-Up via Cashu Plugin Wallet UI

If the Cashu plugin is installed:

1. Go to **Store Settings → Wallets → Cashu**
2. Use the Cashu wallet interface to:
   - Export tokens (to receive ecash)
   - View balance
   - Manage mints

**Note**: Proofs stored via Cashu plugin UI will be available to Bitcoin Rewards plugin (they share the same database).

### Method 3: Receive Cashu Tokens

You can receive Cashu tokens (ecash) from external sources:

1. Someone sends you a Cashu token
2. Import it via Cashu plugin UI (if installed)
3. The proofs will be stored in the database
4. Bitcoin Rewards plugin can use these proofs

## Plugin Conflicts and Compatibility

### If Cashu Plugin IS Installed

**Current Behavior:**
- ✅ **Shared Database**: Both plugins use the same `CashuDbContext` and `Proofs` table
- ✅ **Store Isolation**: Proofs are filtered by `StoreId`, so each store's proofs are separate
- ✅ **Mint URL Sharing**: Both plugins can use the same mint URL from store's Cashu payment method config
- ⚠️ **Potential Issues**:
  - Both plugins can see each other's proofs (but filtered by StoreId)
  - If you manually export tokens via Cashu plugin UI, Bitcoin Rewards can use them
  - If Bitcoin Rewards mints proofs, Cashu plugin can see them in the wallet UI

**This is actually beneficial** - you can:
- Top up via Cashu plugin UI manually
- Use the same balance for both plugins
- Manage everything from one place

### If Cashu Plugin is NOT Installed

**Current Behavior:**
- ❌ **No Proof Persistence**: Proofs cannot be stored in database
- ❌ **No Balance Checking**: Cannot check existing ecash balance
- ✅ **Can Still Mint**: Can mint from Lightning (NUT-04) but proofs won't be saved
- ⚠️ **Limited Functionality**: 
  - Each reward would require a fresh Lightning payment
  - Cannot reuse proofs from previous rewards
  - Cannot check balance before minting

**Recommendation**: Install Cashu plugin for full functionality.

## Recommended Setup

### Option 1: Use Cashu Plugin (Recommended)

1. **Install Cashu Plugin**: Provides database, UI, and full Cashu functionality
2. **Configure Cashu Payment Method**: Set up trusted mints in store settings
3. **Bitcoin Rewards Plugin**: Automatically uses Cashu plugin's database
4. **Benefits**:
   - Shared proof storage
   - Can top up manually via Cashu UI
   - Can view balance in Cashu wallet
   - Both plugins work together seamlessly

### Option 2: Standalone (Not Recommended)

1. **Don't Install Cashu Plugin**: Bitcoin Rewards works independently
2. **Limitations**:
   - No proof persistence
   - No balance checking
   - Each reward requires fresh Lightning payment
   - Cannot reuse proofs

## Future Improvements

To make Bitcoin Rewards plugin fully standalone:

1. **Create Own Database Context**: Add `Proofs` table to `BitcoinRewardsPluginDbContext`
2. **Store Proofs Independently**: Don't rely on Cashu plugin's database
3. **Add Wallet UI**: Create Bitcoin Rewards-specific wallet interface
4. **Benefits**:
   - No dependency on Cashu plugin
   - Complete isolation
   - Can have different mints per plugin

**Current Status**: Bitcoin Rewards plugin works best when Cashu plugin is installed.

## Top-Up Workflow Summary

```
┌─────────────────────────────────────────────────────────┐
│  Reward Request (e.g., 100 sat)                        │
└──────────────────┬──────────────────────────────────────┘
                   │
                   ▼
        ┌──────────────────────┐
        │ Check Ecash Balance  │
        │ (from database)      │
        └──────────┬───────────┘
                   │
        ┌──────────┴───────────┐
        │                      │
        ▼                      ▼
   Sufficient?            Insufficient?
        │                      │
        │                      ▼
        │            ┌──────────────────────┐
        │            │ Check Lightning      │
        │            │ Balance               │
        │            └──────────┬───────────┘
        │                       │
        │            ┌──────────┴───────────┐
        │            │                      │
        │            ▼                      ▼
        │      Sufficient?            Insufficient?
        │            │                      │
        │            │                      ▼
        │            │            ❌ Return Error
        │            │
        │            ▼
        │    ┌──────────────────────┐
        │    │ Mint from Lightning  │
        │    │ (NUT-04)             │
        │    │ - Create quote       │
        │    │ - Pay invoice        │
        │    │ - Get proofs         │
        │    │ - Store in DB        │
        │    └──────────┬───────────┘
        │               │
        └───────────────┼───────────────┐
                        │               │
                        ▼               ▼
              ┌─────────────────────────────┐
              │ Swap/Combine Proofs          │
              │ (NUT-03)                    │
              └──────────┬──────────────────┘
                         │
                         ▼
              ┌─────────────────────────────┐
              │ Create Cashu Token           │
              └──────────┬──────────────────┘
                         │
                         ▼
              ┌─────────────────────────────┐
              │ Send Email with Token        │
              └─────────────────────────────┘
```

## Questions?

- **Q: Can I top up manually?**  
  A: Yes, if Cashu plugin is installed, use its wallet UI. Otherwise, top-up happens automatically when creating rewards.

- **Q: Will proofs conflict between plugins?**  
  A: No, proofs are filtered by `StoreId`. Each store's proofs are isolated.

- **Q: Can I use different mints for each plugin?**  
  A: Currently both use the same mint URL from store's Cashu payment method config. Future versions may support separate mints.

- **Q: What if I don't want to install Cashu plugin?**  
  A: Bitcoin Rewards will work but without proof persistence. Each reward will require a fresh Lightning payment.

