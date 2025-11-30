# Fix Proof Retrieval and Complete Ecash Integration

## Current Status

The Bitcoin Rewards plugin integration with Cashu is 95% complete. The system successfully:
- Discovers Cashu plugin services via reflection ✅
- Retrieves mint URL from store configuration ✅
- Detects ecash balance (21 sats confirmed) ✅
- Identifies sufficient balance for swap operation ✅

**Critical Issue**: `GetStoredProofsAsync()` returns no proofs despite `GetEcashBalanceAsync()` finding them. This prevents swap operations and token creation.

## Objective

Fix proof retrieval in `GetStoredProofsAsync()` to enable swap operations and complete the end-to-end reward flow.

## Implementation Plan

### Phase 1: Diagnose Proof Retrieval Issue

#### Step 1.1: Compare Filtering Logic
**File**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs`

- Review `GetEcashBalanceAsync()` proof filtering (lines 698-740)
- Review `GetStoredProofsAsync()` proof filtering (lines 1214-1280)
- Document differences in filtering criteria
- Identify why one method finds proofs and the other doesn't

**Reference**: Cashu plugin implementation in `Plugins/BTCPayServer.Plugins.Cashu/Controllers/CashuControler.cs` (lines 244-249)

#### Step 1.2: Add Detailed Logging
**File**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs`

Add logging to `GetStoredProofsAsync()` to track:
- Total proofs retrieved from database
- Proofs matching store ID
- Proofs matching keyset IDs
- Proofs excluded due to FailedTransactions
- Final selected proofs count

**Location**: After line 1157 (after proofsList is retrieved)

#### Step 1.3: Verify Keyset ID Comparison
**File**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs`

- Ensure `GetStoredProofsAsync()` uses same keyset ID comparison as `GetEcashBalanceAsync()`
- Verify keyset IDs are retrieved correctly (lines 1058-1068)
- Check if keyset ID comparison logic matches (lines 1218-1250)

### Phase 2: Fix Proof Retrieval

#### Step 2.1: Align Filtering Logic
**File**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs`

Update `GetStoredProofsAsync()` filtering to match `GetEcashBalanceAsync()`:
- Use same store ID comparison
- Use same keyset ID comparison (object comparison, not string)
- Use same FailedTransactions exclusion logic
- Ensure proof selection matches balance calculation logic

**Reference Implementation**: `Plugins/BTCPayServer.Plugins.Cashu/Controllers/CashuControler.cs` (lines 244-249)

#### Step 2.2: Fix Proof Selection Logic
**File**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs`

Review proof selection algorithm (lines 1214-1280):
- Ensure proofs are selected up to `maxAmount`
- Verify amount calculation matches balance check
- Check if proof selection is too restrictive

### Phase 3: Verify Swap Operation

#### Step 3.1: Test Proof Conversion
**File**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs`

Verify `SwapProofsAsync()` (lines 1281-1400):
- Ensure `ToDotNutProof()` is called correctly on each proof
- Verify converted proofs are valid `DotNut.Proof` objects
- Check proof amounts sum correctly

#### Step 3.2: Test Swap Method Call
**File**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs`

- Verify `CashuWallet.Swap()` is called with correct parameters
- Check swap amounts are calculated correctly
- Verify swap response contains `ResultProofs`

**Reference**: `Plugins/BTCPayServer.Plugins.Cashu/CashuAbstractions/CashuWallet.cs` (lines 229-260)

### Phase 4: Test Token Creation

#### Step 4.1: Verify Token Structure
**File**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs`

Test `CreateTokenFromProofs()` (lines 1912-2000):
- Verify `CashuToken` structure matches Cashu plugin format
- Ensure `Tokens` array contains correct `Token` nested class
- Verify `Proofs` are correctly converted from swap results
- Check `Memo` and `Unit` are set correctly

**Reference**: `Plugins/BTCPayServer.Plugins.Cashu/Controllers/CashuControler.cs` (lines 251-266)

#### Step 4.2: Test Token Encoding
**File**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs`

- Verify `CashuToken.Encode()` produces valid token string
- Test token can be decoded by Cashu wallet
- Verify token format matches Cashu protocol

### Phase 5: End-to-End Testing

#### Step 5.1: Pre-Testing Setup
- Verify Cashu plugin installed and configured
- Verify store has Cashu payment method with trusted mint
- Verify wallet has ecash balance (21+ sats)
- Verify email plugin configured (if using email)

#### Step 5.2: Test Complete Flow
1. Create test reward via UI (0.1 USD, test@example.com)
2. Monitor logs for complete flow:
   - Mint URL retrieval
   - Balance detection
   - Proof retrieval
   - Swap operation
   - Token creation
   - Email delivery
3. Verify reward record in database
4. Verify email received with token
5. Test token redemption in Cashu wallet

#### Step 5.3: Verify Error Handling
- Test with insufficient balance
- Test with no proofs available
- Test with Lightning client unavailable
- Verify graceful error handling and logging

## Success Criteria

1. ✅ `GetStoredProofsAsync()` returns proofs when balance exists
2. ✅ Swap operation succeeds with retrieved proofs
3. ✅ Token is created from swapped proofs
4. ✅ Email is sent with token
5. ✅ Token can be redeemed in Cashu wallet
6. ✅ Complete end-to-end flow works without errors

## Key Files

- **Main Integration**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs`
- **Reference Implementation**: `Plugins/BTCPayServer.Plugins.Cashu/Controllers/CashuControler.cs`
- **Wallet Operations**: `Plugins/BTCPayServer.Plugins.Cashu/CashuAbstractions/CashuWallet.cs`
- **Documentation**: `BITCOIN_REWARDS_ECASH_INTEGRATION_STATUS.md`

## Notes

- All reflection-based method calls have been fixed
- Balance detection is working correctly
- Main issue is proof retrieval filtering logic
- Compare working implementation (`GetEcashBalanceAsync`) with non-working (`GetStoredProofsAsync`)
- Use Cashu plugin's `ExportMintBalance` as reference for correct filtering

