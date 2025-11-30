# Bitcoin Rewards Ecash Integration - Status and Next Steps

## Executive Summary

This document provides a comprehensive overview of the Bitcoin Rewards plugin integration with the Cashu (BTCNutServer) plugin for issuing ecash rewards via email. The integration uses reflection to avoid compile-time dependencies on the Cashu plugin, allowing the Bitcoin Rewards plugin to work independently while leveraging Cashu's ecash minting capabilities.

**Current Status**: Integration is 95% complete. The system successfully detects ecash balance (21 sats confirmed) and attempts to use Swap (NUT-03) operations, but `GetStoredProofsAsync` is not returning proofs for the swap operation despite the balance check finding them.

**Critical Issue**: Proof retrieval for swap operations is failing, preventing token creation from existing ecash balance.

---

## Work Completed

### 1. Reflection-Based Service Discovery

**File**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs`

- Implemented dynamic discovery of Cashu plugin services using reflection
- Services discovered:
  - `CashuDbContextFactory` - Database context factory for Cashu plugin
  - `LightningClientFactoryService` - Lightning network client factory
  - `PaymentMethodHandlerDictionary` - Payment method handlers
  - `CashuWallet` type - Core wallet operations
  - `CashuPaymentService` - Payment processing service

**Key Methods**:
- `TryDiscoverCashuService()` - Discovers and caches Cashu plugin services
- Service discovery happens at construction time and is cached

### 2. Mint URL Retrieval

**File**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs` (lines 200-450)

- Implemented `GetMintUrlForStore()` method with multiple fallback strategies:
  1. **Primary**: Uses reflection to find `GetPaymentMethodConfig<T>` extension method from `StoreDataExtensions`
  2. **Fallback**: Searches for `StoreDataExtensions` in all loaded assemblies
  3. **Database Fallback**: Queries `Mints` table in Cashu plugin database

**Status**: ✅ **WORKING** - Successfully retrieves mint URL: `https://mint.minibits.cash/Bitcoin`

**Key Implementation Details**:
- Handles generic method invocation via reflection
- Supports both 3-parameter and 4-parameter versions of `GetPaymentMethodConfig<T>`
- Includes extensive logging for troubleshooting

### 3. Ecash Balance Detection

**File**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs` (lines 496-746)

- Implemented `GetEcashBalanceAsync()` method
- Retrieves proofs from Cashu plugin database
- Filters by:
  - Store ID
  - Active keyset IDs (fetched from mint)
  - Excludes proofs in `FailedTransactions`
- Sums proof amounts to calculate total balance

**Status**: ✅ **WORKING** - Successfully detects 21 sats balance

**Key Implementation Details**:
- Uses reflection to call `CreateContext()` with optional parameter handling
- Retrieves keysets from mint via `CashuHttpClient.GetKeysets()`
- Handles `ToListAsync` with CancellationToken parameter support
- Compares `KeysetId` objects directly (not strings) for accurate filtering

### 4. Token Minting Logic

**File**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs` (lines 1860-2000)

- Implemented `MintTokenAsync()` with priority-based strategy:
  1. **First Priority**: Use existing ecash balance via Swap (NUT-03)
  2. **Fallback**: Mint from Lightning (NUT-04) if ecash balance insufficient

**Status**: ⚠️ **PARTIALLY WORKING** - Balance detection works, but swap operation fails

**Key Implementation Details**:
- Checks `ecashBalance >= amountSatoshis` before attempting swap
- Falls back to Lightning minting only if swap fails or balance is insufficient
- Logs detailed information at each step

### 5. Reflection Method Resolution Fixes

**Files**: Multiple locations in `CashuServiceAdapter.cs`

Fixed multiple `AmbiguousMatchException` and `TargetParameterCountException` errors:

#### a. `CreateContext` Method (3 occurrences)
- **Issue**: Ambiguous match for optional `Action<NpgsqlDbContextOptionsBuilder>` parameter
- **Fix**: Explicitly finds method with optional parameter, handles both 0-param and 1-param overloads
- **Locations**: 
  - `GetEcashBalanceAsync()` (line 506)
  - `GetStoredProofsAsync()` (line 1070)
  - `SwapProofsAsync()` (line 1070)

#### b. `GetNetwork` Method (3 occurrences)
- **Issue**: Ambiguous match for `GetNetwork(string)` method
- **Fix**: Filters methods by return type (`BTCPayNetwork`) and parameter type
- **Locations**:
  - `GetLightningBalanceAsync()` (line 830)
  - `GetLightningClientForStore()` (line 1498)
  - `MintFromLightningAsync()` (line 1754)

#### c. `ToListAsync` Method (4 occurrences)
- **Issue**: Parameter count mismatch - method requires `CancellationToken` parameter
- **Fix**: Handles both 1-parameter (IQueryable) and 2-parameter (IQueryable + CancellationToken) overloads
- **Locations**:
  - `GetEcashBalanceAsync()` (line 671)
  - `GetMintUrlForStore()` database fallback (line 405)
  - `GetStoredProofsAsync()` (line 1123)
  - `GetStoredProofsAsync()` FailedTransactions query (line 1170)

#### d. `GetKeysets` Method
- **Issue**: Parameter count mismatch
- **Fix**: Handles both 0-parameter and 1-parameter (CancellationToken) overloads
- **Location**: `GetEcashBalanceAsync()` (line 593)

### 6. Database Migration

**File**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Data/BitcoinRewardsMigrationRunner.cs`

- Implemented hosted service to run Entity Framework Core migrations on startup
- Created `BitcoinRewardRecords` table in dedicated schema
- Added fallback mechanism to manually create table if EF Core migration fails
- Includes explicit transaction management for DDL operations

**Status**: ✅ **WORKING** - Table created successfully

### 7. Token Creation

**File**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs` (lines 1912-2000)

- Implemented `CreateTokenFromProofs()` method
- Creates `CashuToken` structure:
  - `Tokens` array with `Token` nested class
  - `Mint` URL
  - `Proofs` array (converted from `StoredProof` to `DotNut.Proof`)
  - `Unit` ("sat")
  - `Memo` ("Bitcoin Rewards Token")
- Encodes token using `CashuToken.Encode()` method

**Status**: ✅ **IMPLEMENTED** - Not yet tested in production flow

---

## Troubleshooting History

### Issue 1: Database Migration Failure
**Symptom**: `relation "BTCPayServer.Plugins.BitcoinRewards.BitcoinRewardRecords" does not exist`

**Root Cause**: EF Core migration was not creating the table despite reporting success

**Resolution**:
- Added explicit table creation fallback using raw SQL
- Implemented transaction management for DDL operations
- Added verification step to confirm table exists after migration

**Files Modified**:
- `Plugins/BTCPayServer.Plugins.BitcoinRewards/Data/BitcoinRewardsMigrationRunner.cs`

### Issue 2: Mint URL Not Found
**Symptom**: `No mint URL found for store... Please configure Cashu payment method with trusted mints`

**Root Cause**: Reflection-based method discovery was not finding `GetPaymentMethodConfig<T>` extension method

**Resolution**:
- Searched for `StoreDataExtensions` in all loaded assemblies
- Implemented generic method invocation with proper type resolution
- Added database fallback to query `Mints` table
- Fixed `CreateContext` ambiguous method call

**Files Modified**:
- `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs` (lines 200-450)

### Issue 3: Incorrect Ecash Balance (1 sat instead of 21 sats)
**Symptom**: Balance check returning 1 sat when wallet had 21 sats

**Root Cause**: `KeysetId` objects were being compared as strings instead of using object comparison

**Resolution**:
- Changed comparison to use direct object comparison and `Equals()` method
- Fixed in both `GetEcashBalanceAsync()` and `GetStoredProofsAsync()`

**Files Modified**:
- `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs` (lines 714-740, 1218-1250)

### Issue 4: Multiple Reflection Ambiguity Errors
**Symptom**: `AmbiguousMatchException` and `TargetParameterCountException` errors

**Root Cause**: Reflection was finding multiple method overloads and couldn't determine which to use

**Resolution**:
- Implemented explicit method filtering by parameter types and return types
- Added handling for optional parameters
- Added support for `CancellationToken` parameters in async methods

**Files Modified**:
- `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs` (multiple locations)

### Issue 5: "No proofs available for swapping"
**Symptom**: Balance check finds 21 sats, but `GetStoredProofsAsync` returns no proofs

**Root Cause**: `ToListAsync` method lookup in `GetStoredProofsAsync` was using old approach that didn't handle CancellationToken parameter

**Status**: ⚠️ **IN PROGRESS** - Fixed `ToListAsync` lookup, but issue may persist due to filtering logic differences

**Files Modified**:
- `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs` (lines 1123-1200)

---

## Current State

### What's Working

1. ✅ **Service Discovery**: Cashu plugin services are discovered and cached
2. ✅ **Mint URL Retrieval**: Successfully retrieves mint URL from payment method config
3. ✅ **Ecash Balance Detection**: Correctly detects 21 sats in wallet
4. ✅ **Balance Comparison**: Correctly identifies sufficient balance (21 sats >= 1 sat needed)
5. ✅ **Swap Attempt**: System attempts Swap (NUT-03) operation when balance is sufficient
6. ✅ **Database Migration**: `BitcoinRewardRecords` table created successfully
7. ✅ **Reflection Fixes**: All ambiguous method calls resolved

### What's Not Working

1. ❌ **Proof Retrieval for Swap**: `GetStoredProofsAsync()` returns no proofs despite balance check finding them
2. ❌ **Token Creation**: Cannot create token because swap operation fails
3. ❌ **Lightning Fallback**: Lightning client not available (expected - not configured)

### Current Error Flow

```
1. Mint URL found: ✅ https://mint.minibits.cash/Bitcoin
2. Ecash balance detected: ✅ 21 sat
3. Sufficient balance identified: ✅ 21 sat >= 1 sat needed
4. Swap operation attempted: ✅ "Sufficient ecash balance, using Swap (NUT-03)"
5. Proof retrieval fails: ❌ "No proofs available for swapping 1 sat"
6. Falls back to Lightning: ⚠️ Lightning client not available
7. Token creation fails: ❌ "Failed to mint ecash token"
```

---

## Next Steps for End-to-End Testing

### Step 1: Fix Proof Retrieval in `GetStoredProofsAsync`

**Problem**: The method is not returning proofs even though `GetEcashBalanceAsync` finds them.

**Investigation Required**:

1. **Compare Filtering Logic**:
   - Review `GetEcashBalanceAsync()` proof filtering (lines 698-740)
   - Review `GetStoredProofsAsync()` proof filtering (lines 1214-1280)
   - Identify differences in filtering criteria

2. **Check Keyset ID Comparison**:
   - Verify `KeysetId` comparison logic in both methods
   - Ensure both methods use the same comparison approach
   - Check if keyset IDs are being retrieved correctly in `GetStoredProofsAsync`

3. **Verify Database Query**:
   - Add detailed logging to `GetStoredProofsAsync` to see:
     - How many proofs are retrieved from database
     - Which proofs match the store ID
     - Which proofs match the keyset IDs
     - Which proofs are excluded due to FailedTransactions

**Files to Review**:
- `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs`:
  - `GetEcashBalanceAsync()` (lines 496-746)
  - `GetStoredProofsAsync()` (lines 1015-1280)

**Reference Implementation**:
- `Plugins/BTCPayServer.Plugins.Cashu/Controllers/CashuControler.cs` (lines 244-249) shows how the Cashu plugin filters proofs:
  ```csharp
  var selectedProofs = db.Proofs.Where(p=>
      p.StoreId == StoreData.Id 
      && keysets.Select(k => k.Id).Contains(p.Id) 
      && !db.FailedTransactions.Any(ft => ft.UsedProofs.Contains(p))
  ).ToList();
  ```

### Step 2: Verify Swap Operation

**After fixing proof retrieval**, verify the swap operation:

1. **Test Swap Method Call**:
   - Ensure `CashuWallet.Swap()` is called correctly
   - Verify proof conversion from `StoredProof` to `DotNut.Proof` using `ToDotNutProof()`
   - Check that swap amounts are calculated correctly

2. **Handle Swap Errors**:
   - Add error handling for swap failures
   - Log detailed error messages from swap operation
   - Verify swap response contains `ResultProofs`

**Files to Review**:
- `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs`:
  - `SwapProofsAsync()` (lines 1281-1400)
- `Plugins/BTCPayServer.Plugins.Cashu/CashuAbstractions/CashuWallet.cs`:
  - `Swap()` method (lines 229-260)

### Step 3: Test Token Creation

**After swap succeeds**, verify token creation:

1. **Verify Proof Conversion**:
   - Ensure `ToDotNutProof()` method is called correctly
   - Verify converted proofs are valid `DotNut.Proof` objects

2. **Test Token Encoding**:
   - Verify `CashuToken.Encode()` produces valid token string
   - Test token can be decoded by Cashu wallet

**Files to Review**:
- `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs`:
  - `CreateTokenFromProofs()` (lines 1912-2000)
- `Plugins/BTCPayServer.Plugins.Cashu/Controllers/CashuControler.cs`:
  - `ExportMintBalance()` (lines 251-266) - reference implementation

### Step 4: Test Email Notification

**After token creation succeeds**, verify email delivery:

1. **Test Email Service**:
   - Verify `EmailNotificationService` can send emails
   - Test with real email address
   - Verify token is included in email body

**Files to Review**:
- `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/EmailNotificationService.cs`

### Step 5: End-to-End Test

**Complete flow test**:

1. **Prerequisites**:
   - Cashu plugin installed and configured
   - Store has Cashu payment method with trusted mint URL
   - Wallet has sufficient ecash balance (21+ sats confirmed)
   - Email plugin configured (if using email delivery)

2. **Test Steps**:
   - Navigate to Bitcoin Rewards plugin UI
   - Create test reward with:
     - Amount: 0.1 USD (or any amount)
     - Email: test@example.com
   - Verify reward is created in database
   - Verify token is minted/created
   - Verify email is sent with token
   - Verify token can be redeemed in Cashu wallet

3. **Expected Log Flow**:
   ```
   - "Minting token for X sat in store..."
   - "Found mint URL from payment method config..."
   - "Ecash balance for store...: Y sat (needed: X sat)"
   - "Sufficient ecash balance, using Swap (NUT-03)"
   - "Retrieved N proofs for swapping"
   - "Token created successfully from proofs"
   - "Reward processed successfully"
   ```

---

## Potential Ecash Limitations

### 1. Cashu Protocol Limitations

#### a. Single Mint Tokens Only
- **Limitation**: Cashu v4 protocol only supports single-mint tokens
- **Impact**: Cannot create tokens with proofs from multiple mints
- **Reference**: `Plugins/BTCPayServer.Plugins.Cashu/CashuAbstractions/CashuUtils.cs` (line 94-96)
- **Workaround**: Ensure all proofs come from the same mint

#### b. Keyset Requirements
- **Limitation**: Proofs must match active keysets from the mint
- **Impact**: Proofs from inactive/expired keysets cannot be used
- **Reference**: `Plugins/BTCPayServer.Plugins.Cashu/Controllers/CashuControler.cs` (line 246)
- **Workaround**: Always fetch current keysets before filtering proofs

#### c. Failed Transaction Exclusion
- **Limitation**: Proofs used in failed transactions cannot be reused
- **Impact**: Some proofs may be locked and unavailable
- **Reference**: `Plugins/BTCPayServer.Plugins.Cashu/Controllers/CashuControler.cs` (line 248)
- **Workaround**: System already excludes these proofs

### 2. Mint Limitations

#### a. Mint Availability
- **Limitation**: Mint must be online and accessible
- **Impact**: Cannot mint/swap if mint is down
- **Reference**: `Plugins/BTCPayServer.Plugins.Cashu/Controllers/CashuControler.cs` (lines 184-187)
- **Workaround**: Implement retry logic with exponential backoff

#### b. Mint Rate Limits
- **Limitation**: Mints may have rate limits on swap/mint operations
- **Impact**: High-frequency operations may be throttled
- **Workaround**: Implement rate limiting and queuing

#### c. Mint Fee Structure
- **Limitation**: Mints may charge fees for swap/mint operations
- **Impact**: Actual token amount may be less than requested
- **Reference**: `Plugins/BTCPayServer.Plugins.Cashu/CashuAbstractions/CashuWallet.cs` (line 56)
- **Workaround**: Account for fees in amount calculations

### 3. Lightning Network Limitations

#### a. Lightning Client Required for Minting
- **Limitation**: Minting from Lightning (NUT-04) requires Lightning client
- **Impact**: Cannot mint if Lightning not configured
- **Reference**: `Plugins/BTCPayServer.Plugins.Cashu/CashuAbstractions/CashuWallet.cs` (line 31)
- **Workaround**: Ensure Lightning is configured for fallback minting

#### b. Lightning Balance Requirements
- **Limitation**: Sufficient Lightning balance needed for minting
- **Impact**: Cannot mint if Lightning balance insufficient
- **Workaround**: Monitor Lightning balance and top up as needed

### 4. Database Limitations

#### a. Proof Storage
- **Limitation**: Proofs are stored per store and mint
- **Impact**: Cannot share proofs across stores
- **Reference**: `Plugins/BTCPayServer.Plugins.Cashu/Data/Models/StoredProof.cs`
- **Workaround**: Each store maintains its own proof pool

#### b. Transaction Isolation
- **Limitation**: Proofs are removed from database when exported
- **Impact**: Cannot reuse exported proofs
- **Reference**: `Plugins/BTCPayServer.Plugins.Cashu/Controllers/CashuControler.cs` (lines 289-290)
- **Workaround**: System creates new proofs via swap before exporting

### 5. Reflection-Based Integration Limitations

#### a. Runtime Type Discovery
- **Limitation**: All type discovery happens at runtime
- **Impact**: Type mismatches only discovered at runtime
- **Workaround**: Extensive logging and error handling

#### b. Method Signature Changes
- **Limitation**: Cashu plugin updates may break reflection calls
- **Impact**: Integration may break if Cashu plugin changes method signatures
- **Workaround**: Version checking and graceful degradation

#### c. Performance Overhead
- **Limitation**: Reflection has performance overhead
- **Impact**: Slightly slower than direct method calls
- **Workaround**: Cache discovered types and methods

### 6. Amount Limitations

#### a. Minimum Amount
- **Limitation**: Minimum reward is 1 satoshi
- **Impact**: Cannot issue rewards smaller than 1 sat
- **Reference**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/BitcoinRewardsService.cs` (line 246)
- **Workaround**: Round up to 1 sat minimum

#### b. Maximum Amount
- **Limitation**: Limited by ecash balance + Lightning balance
- **Impact**: Cannot issue rewards larger than available balance
- **Workaround**: Check balance before processing reward

#### c. Proof Selection
- **Limitation**: Must select proofs that sum to exact amount (or use split)
- **Impact**: May need to swap larger proofs to get exact amount
- **Reference**: `Plugins/BTCPayServer.Plugins.Cashu/CashuAbstractions/CashuUtils.cs` (line 110)
- **Workaround**: Use `CashuUtils.SelectProofs()` for optimal selection

---

## Testing Checklist

### Pre-Testing Setup

- [ ] Cashu plugin installed and enabled
- [ ] Store configured with Cashu payment method
- [ ] Trusted mint URL configured: `https://mint.minibits.cash/Bitcoin`
- [ ] Wallet has ecash balance (21+ sats confirmed working)
- [ ] Email plugin configured (if using email delivery)
- [ ] Bitcoin Rewards plugin installed and enabled
- [ ] Store settings configured:
  - [ ] Reward percentage set (0.1% recommended for testing)
  - [ ] Delivery method selected (Email/SMS)
  - [ ] Minimum transaction amount (if applicable)

### Testing Steps

1. [ ] **Verify Service Discovery**
   - Check logs for: "Cashu plugin services discovered successfully"
   - Verify no reflection errors

2. [ ] **Verify Mint URL Retrieval**
   - Check logs for: "Found mint URL from payment method config"
   - Verify mint URL is correct

3. [ ] **Verify Balance Detection**
   - Check logs for: "Ecash balance for store...: X sat"
   - Verify balance matches wallet balance

4. [ ] **Test Reward Creation**
   - Create test reward via UI
   - Amount: 0.1 USD
   - Email: test@example.com
   - Verify reward record created in database

5. [ ] **Verify Proof Retrieval**
   - Check logs for: "Retrieved N proofs for swapping"
   - Verify N > 0

6. [ ] **Verify Swap Operation**
   - Check logs for swap success
   - Verify new proofs created

7. [ ] **Verify Token Creation**
   - Check logs for: "Token created successfully from proofs"
   - Verify token string is not null/empty

8. [ ] **Verify Email Delivery**
   - Check email inbox for reward notification
   - Verify token is included in email

9. [ ] **Verify Token Redemption**
   - Copy token from email
   - Test in Cashu wallet (e.g., Minibits wallet)
   - Verify token can be redeemed

10. [ ] **Verify Database Records**
    - Check `BitcoinRewardRecords` table
    - Verify reward status is "Sent"
    - Verify `EcashToken` field is populated

---

## Key Files Reference

### Core Integration Files

1. **`Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs`**
   - Main integration adapter using reflection
   - ~2258 lines
   - Key methods:
     - `TryDiscoverCashuService()` - Service discovery
     - `GetMintUrlForStore()` - Mint URL retrieval
     - `GetEcashBalanceAsync()` - Balance detection
     - `GetStoredProofsAsync()` - Proof retrieval (needs fix)
     - `SwapProofsAsync()` - Swap operation
     - `MintFromLightningAsync()` - Lightning minting
     - `MintTokenAsync()` - Main minting orchestration
     - `CreateTokenFromProofs()` - Token creation

2. **`Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/BitcoinRewardsService.cs`**
   - Main reward processing service
   - ~255 lines
   - Key method: `ProcessRewardAsync()` - Orchestrates reward flow

3. **`Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/EmailNotificationService.cs`**
   - Email notification service
   - Uses reflection to interact with BTCPay Email plugin

### Cashu Plugin Reference Files

1. **`Plugins/BTCPayServer.Plugins.Cashu/Controllers/CashuControler.cs`**
   - Reference implementation for token export
   - `ExportMintBalance()` method (lines 218-302) shows how to:
     - Filter proofs
     - Create tokens
     - Encode tokens

2. **`Plugins/BTCPayServer.Plugins.Cashu/CashuAbstractions/CashuWallet.cs`**
   - Core wallet operations
   - `Swap()` method (lines 229-260) - Swap implementation

3. **`Plugins/BTCPayServer.Plugins.Cashu/CashuAbstractions/CashuUtils.cs`**
   - Utility functions
   - `SelectProofs()` - Proof selection algorithm
   - `SimplifyToken()` - Token simplification

### Database Files

1. **`Plugins/BTCPayServer.Plugins.BitcoinRewards/Data/BitcoinRewardsMigrationRunner.cs`**
   - Database migration runner
   - Creates `BitcoinRewardRecords` table

2. **`Plugins/BTCPayServer.Plugins.BitcoinRewards/Data/BitcoinRewardsPluginDbContext.cs`**
   - Entity Framework DbContext
   - Defines `BitcoinRewardRecords` entity

---

## Debugging Tips

### Enable Detailed Logging

Add to `appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "BTCPayServer.Plugins.BitcoinRewards": "Information",
      "BTCPayServer.Plugins.BitcoinRewards.Services.CashuServiceAdapter": "Debug"
    }
  }
}
```

### Key Log Messages to Monitor

1. **Service Discovery**:
   - "Cashu plugin assembly found"
   - "CashuDbContextFactory found and resolved"
   - "Cashu plugin services discovered successfully"

2. **Mint URL**:
   - "Found mint URL from payment method config"
   - "Found mint URL from database"

3. **Balance**:
   - "Ecash balance for store...: X sat (needed: Y sat)"
   - "Found N proofs matching keysets"

4. **Swap**:
   - "Sufficient ecash balance, using Swap (NUT-03)"
   - "Retrieved N proofs for swapping"
   - "Swap operation failed"

5. **Token**:
   - "Token created successfully from proofs"
   - "Failed to create token from proofs"

### Common Issues and Solutions

1. **"No mint URL found"**
   - Verify Cashu payment method is configured in store settings
   - Check trusted mint URLs are set
   - Verify store ID is correct

2. **"Ecash balance: 0 sat"**
   - Verify wallet has proofs in database
   - Check keyset IDs match active keysets
   - Verify store ID matches proof store ID

3. **"No proofs available for swapping"**
   - Compare filtering logic between `GetEcashBalanceAsync` and `GetStoredProofsAsync`
   - Verify keyset ID comparison is consistent
   - Check if proofs are excluded due to FailedTransactions

4. **"Lightning client not available"**
   - Expected if Lightning not configured
   - Only needed for fallback minting
   - Not critical if ecash balance is sufficient

---

## Build and Deployment

### Build Command

```bash
cd /Users/jp/git/bitcoinrewards
dotnet build Plugins/BTCPayServer.Plugins.BitcoinRewards/BTCPayServer.Plugins.BitcoinRewards.csproj -c Release
```

### Create Plugin Package

```bash
cd Plugins/BTCPayServer.Plugins.BitcoinRewards/bin/Release/net8.0
zip -r ../../../../BTCPayServer.Plugins.BitcoinRewards.btcpay \
  BTCPayServer.Plugins.BitcoinRewards.dll \
  BTCPayServer.Plugins.BitcoinRewards.pdb \
  BTCPayServer.Plugins.BitcoinRewards.deps.json \
  BTCPayServer.Plugins.BitcoinRewards.staticwebassets.endpoints.json \
  CBOR.dll \
  NBip32Fast.dll \
  NBitcoin.Secp256k1.dll \
  Resources/
```

### Deployment

1. Upload `.btcpay` file to BTCPay Server
2. Install plugin via Plugins page
3. Enable plugin for store
4. Configure store settings
5. Test reward creation

---

## Summary

The Bitcoin Rewards ecash integration is nearly complete. The main remaining issue is proof retrieval in `GetStoredProofsAsync()`. Once this is fixed, the system should be able to:

1. Detect ecash balance ✅
2. Retrieve proofs for swapping ⚠️ (needs fix)
3. Perform swap operation ⚠️ (depends on #2)
4. Create token from swapped proofs ⚠️ (depends on #3)
5. Send email with token ⚠️ (depends on #4)

The next agent should focus on comparing the filtering logic between `GetEcashBalanceAsync()` and `GetStoredProofsAsync()` to identify why proofs are not being returned for swap operations.

