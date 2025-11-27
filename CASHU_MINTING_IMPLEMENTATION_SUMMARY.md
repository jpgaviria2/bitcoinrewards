# Cashu Token Minting Implementation - Detailed Summary for Next Agent

## Executive Summary

The BitcoinRewards plugin cannot create Cashu ecash tokens for rewards because the discovered `CashuPaymentService` doesn't expose token minting methods. The solution is to implement wallet-based token creation using Cashu Development Kit (CDK) best practices, specifically using NUT-03 (swapping tokens) to split existing wallet proofs and create tokens.

## Current Problem State

### Issue
- **Error**: `Minting method not found on Cashu service. Available methods: ProcessPaymentAsync, RegisterCashuPayment, AddProofsToDb, PollFailedMelt, PollFailedSwap`
- **Root Cause**: `CashuPaymentService` handles payments but doesn't have methods to mint/create tokens
- **Location**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs` - `MintTokenAsync()` method

### Current Status
- ✅ Cashu plugin is installed and active
- ✅ Cashu wallet has balance (88 sat confirmed)
- ✅ Cashu service is discovered and accessible
- ❌ Cannot mint/create tokens for rewards

### Current Service Discovery
The `TryDiscoverCashuService()` method currently:
1. Looks for Cashu plugin assembly
2. Tries multiple service type names (CashuService, CashuWallet, CashuWalletService, etc.)
3. Falls back to pattern-based search
4. **Problem**: Finds `CashuPaymentService` which doesn't have minting methods

## Cashu CDK Best Practices Alignment

Based on the [Cashu Development Kit (CDK)](https://github.com/cashubtc/cdk), the proper approach is:

### Key CDK Concepts

1. **NUT-03 (Swapping Tokens)**: 
   - Wallets swap/split existing proofs into smaller denominations
   - This is what we need for rewards (split existing proofs)

2. **NUT-04 (Minting Tokens)**: 
   - Mints create NEW tokens from Lightning payments
   - Not needed here since we have existing proofs

3. **Wallet vs Mint**:
   - **Wallet**: Manages proofs, can swap/split them to create new tokens
   - **Mint**: Creates new tokens from Lightning payments
   - **For rewards**: We need wallet swap functionality, not minting

### CDK Implementation Pattern

According to CDK standards:
- Wallets have `Swap`/`SwapAsync` methods to split proofs
- Swapped proofs are then converted to Cashu token format
- Token format is base64-encoded JSON following Cashu specification

## Solution Architecture

### Strategy: Use Wallet Swap (NUT-03) Instead of Direct Minting

Since the wallet has existing proofs (88 sat confirmed):
1. Discover the Cashu **wallet service** (separate from payment service)
2. Use wallet's **Swap/SwapAsync** method to split proofs into reward amount
3. Create Cashu token from the swapped proofs
4. Return token string for reward delivery

### Implementation Approach

#### Phase 1: Enhanced Service Discovery
**Goal**: Find wallet service with Swap methods

**Changes to `TryDiscoverCashuService()`**:
1. Prioritize services with "Wallet" in name
2. Check for services with `Swap` or `SwapAsync` methods
3. Store both wallet service and payment service references
4. Log all discovered services and their available methods

**Service Discovery Priority**:
1. Services with `Swap` methods (indicating wallet functionality)
2. Services with "Wallet" in name
3. Services matching CDK wallet patterns
4. Fall back to payment service (last resort, won't work for minting)

#### Phase 2: Implement Wallet Swap-Based Token Creation
**Goal**: Use wallet swap to create tokens

**Changes to `MintTokenAsync()`**:
1. Use wallet service (not payment service) for minting operations
2. Call wallet's `Swap`/`SwapAsync` method to split proofs
3. Convert swapped proofs to Cashu token format
4. Return token string

**Swap Method Signatures to Try**:
```csharp
// Common CDK patterns:
SwapAsync(ulong amount, string mintUrl) → Returns proofs
SwapAsync(long amountSatoshis, string storeId) → Returns proofs  
Swap(ulong amount) → Returns proofs
Task<List<Proof>> SwapAsync(...) → Returns proof list
Task<List<object>> SwapAsync(...) → Returns proof list (object type)
```

#### Phase 3: Token Creation from Proofs
**Goal**: Convert proofs to Cashu token format

**Token Format**:
- Base64-encoded JSON following Cashu token specification
- Format: `cashuAeyJ...` (base64 JSON string)
- Contains proofs, mint URL, and metadata

## Implementation Details

### File to Modify

**Primary File**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs`

### Key Changes Required

#### 1. Service Discovery Enhancement

**Current State** (lines 31-151):
- Single service discovery finds `CashuPaymentService`
- No prioritization of wallet services
- Limited method inspection

**Required Changes**:
- Add wallet service discovery
- Check for Swap methods on discovered services
- Store wallet service reference separately
- Log all discovered services and methods

**Code Pattern**:
```csharp
private object? _cashuWalletService;
private object? _cashuPaymentService;

private void TryDiscoverCashuService() {
    // First: Look for wallet services with Swap methods
    // Second: Look for wallet services by name
    // Third: Store payment service as fallback
    // Log all discovered services and their methods
}
```

#### 2. MintTokenAsync Rewrite

**Current State** (lines 153-253):
- Looks for minting methods on payment service
- Fails because payment service doesn't have minting methods

**Required Changes**:
- Use wallet service instead of payment service
- Call Swap method to split proofs
- Convert proofs to token format
- Return token string

**Code Pattern**:
```csharp
public async Task<string?> MintTokenAsync(long amountSatoshis, string storeId) {
    // 1. Ensure wallet service is discovered
    // 2. Find Swap method on wallet service
    // 3. Call Swap to get proofs for reward amount
    // 4. Convert proofs to Cashu token format
    // 5. Return token string
}
```

#### 3. Swap Method Discovery

**Method Discovery Strategy**:
```csharp
// Try multiple swap method signatures
var swapMethods = new[] {
    ("SwapAsync", new[] { typeof(ulong), typeof(string) }),
    ("SwapAsync", new[] { typeof(long), typeof(string) }),
    ("Swap", new[] { typeof(ulong) }),
    ("SwapProofs", new[] { typeof(ulong) }),
    // Try all methods with "Swap" in name
};
```

#### 4. Proof to Token Conversion

**Token Creation Logic**:
```csharp
// After getting proofs from swap:
// 1. Create token JSON structure with proofs
// 2. Include mint URL and metadata
// 3. Base64 encode the JSON
// 4. Return as "cashuAeyJ..." format
```

#### 5. Enhanced Logging

**Logging Requirements**:
- Log all discovered services and their types
- Log all available methods on each service
- Log Swap method discovery process
- Log swap operation results
- Log token creation steps
- Log errors with full context

## Step-by-Step Implementation Plan

### Step 1: Enhance Service Discovery (Priority: High)

**Task**: Modify `TryDiscoverCashuService()` to find wallet services

**Changes**:
1. Add `_cashuWalletService` field to store wallet service separately
2. Search for services with "Wallet" in name first
3. Check each service for Swap methods
4. Prioritize services with Swap methods
5. Store wallet service if found, payment service as fallback
6. Log all discovered services with their methods

**Success Criteria**:
- Wallet service discovered if available
- Logs show all available services and methods
- Can differentiate between wallet and payment services

### Step 2: Implement Swap-Based Token Creation (Priority: High)

**Task**: Rewrite `MintTokenAsync()` to use wallet swap

**Changes**:
1. Use `_cashuWalletService` instead of `_cashuService`
2. Discover Swap method using reflection
3. Call Swap with reward amount
4. Handle different return types (proofs, proof list, etc.)
5. Convert proofs to token format
6. Return token string

**Success Criteria**:
- Swap method found and called successfully
- Proofs retrieved from swap operation
- Token created from proofs

### Step 3: Add Token Creation Logic (Priority: High)

**Task**: Convert swapped proofs to Cashu token format

**Changes**:
1. Create token JSON structure
2. Include proofs, mint URL, metadata
3. Base64 encode JSON
4. Format as Cashu token string

**Success Criteria**:
- Token format matches Cashu specification
- Token can be validated/decoded
- Token format is compatible with Cashu ecosystem

### Step 4: Add Comprehensive Error Handling (Priority: Medium)

**Task**: Improve error handling and logging

**Changes**:
1. Log service discovery process in detail
2. Log method discovery attempts
3. Log swap operation results
4. Log token creation steps
5. Provide clear error messages

**Success Criteria**:
- Detailed logs help debug issues
- Error messages are actionable
- Failed operations are logged with context

### Step 5: Testing and Validation (Priority: High)

**Task**: Test implementation with actual wallet

**Tests**:
1. Verify wallet service discovery
2. Verify Swap method discovery
3. Verify swap operation with wallet balance
4. Verify token creation and format
5. Verify token can be used for rewards

**Success Criteria**:
- All tests pass
- Token created successfully
- Token format is valid

## Code Reference Locations

### Current Code Structure

**File**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs`

- **Line 17-20**: Service fields (`_cashuServiceAvailable`, `_cashuService`)
- **Line 31-151**: `TryDiscoverCashuService()` - Service discovery logic
- **Line 153-253**: `MintTokenAsync()` - Current minting logic (fails)
- **Line 255-314**: `ReclaimTokenAsync()` - Token reclaim logic
- **Line 316-370**: `ValidateTokenAsync()` - Token validation logic

### Key Methods to Modify

1. **`TryDiscoverCashuService()`** - Enhance to find wallet services
2. **`MintTokenAsync()`** - Complete rewrite to use wallet swap
3. **New helper methods needed**:
   - `DiscoverWalletService()` - Find wallet service specifically
   - `FindSwapMethod()` - Discover Swap method on wallet service
   - `SwapProofsAsync()` - Execute swap operation
   - `CreateTokenFromProofs()` - Convert proofs to token format

## Expected Behavior After Implementation

### Successful Flow

1. **Service Discovery**:
   ```
   INFO: Cashu plugin assembly found
   INFO: Cashu wallet service found: CashuWalletService
   INFO: Wallet service has SwapAsync method
   INFO: Available methods: SwapAsync, GetBalance, GetProofs, ...
   ```

2. **Token Creation**:
   ```
   INFO: Swapping proofs for 1000 sats
   INFO: Swap operation successful, received 2 proofs
   INFO: Creating token from proofs
   INFO: Token created successfully: cashuAeyJ...
   ```

3. **Result**:
   - Token string returned to `BitcoinRewardsService`
   - Token stored in reward record
   - Token sent to customer via email/SMS

### Error Scenarios

1. **No Wallet Service Found**:
   ```
   WARNING: Cashu wallet service not found
   INFO: Discovered services: CashuPaymentService
   ERROR: Cannot mint tokens without wallet service
   ```

2. **No Swap Method Found**:
   ```
   WARNING: Swap method not found on wallet service
   INFO: Available methods: GetBalance, GetProofs, ...
   ERROR: Cannot swap proofs without Swap method
   ```

3. **Insufficient Balance**:
   ```
   WARNING: Wallet balance (88 sat) insufficient for reward (1000 sat)
   ERROR: Cannot create token - insufficient balance
   ```

## CDK Alignment Checklist

Ensure implementation follows CDK standards:

- ✅ Uses NUT-03 (swapping tokens) instead of NUT-04 (minting from Lightning)
- ✅ Works with existing wallet proofs (not creating new tokens from Lightning)
- ✅ Follows wallet service patterns from CDK
- ✅ Creates tokens from proofs (standard Cashu flow)
- ✅ Token format matches Cashu specification
- ✅ Handles errors gracefully with detailed logging

## Testing Strategy

### Unit Testing

1. **Service Discovery Tests**:
   - Test wallet service discovery
   - Test method discovery on wallet service
   - Test fallback to payment service

2. **Swap Operation Tests**:
   - Test Swap method discovery
   - Test swap with different amounts
   - Test swap error handling

3. **Token Creation Tests**:
   - Test proof to token conversion
   - Test token format validation
   - Test token encoding/decoding

### Integration Testing

1. **End-to-End Test**:
   - Create test reward
   - Verify token minting succeeds
   - Verify token format is valid
   - Verify token can be decoded

2. **Error Handling Test**:
   - Test with no wallet service
   - Test with insufficient balance
   - Test with invalid swap method

### Manual Testing

1. **With Real Wallet**:
   - Use actual Cashu wallet with balance
   - Create real reward
   - Verify token is created
   - Verify token works with Cashu clients

## Dependencies and Prerequisites

### Required

- BTCPay Server Cashu plugin installed
- Cashu wallet configured and active
- Wallet has sufficient balance for rewards
- Wallet service accessible via DI

### Optional

- Access to Cashu plugin source code (for reference)
- CDK documentation (for token format reference)
- Cashu token format specification

## Known Constraints

1. **Reflection-Based**: Uses reflection to avoid compile-time dependencies
2. **Service Discovery**: Depends on service being registered in DI
3. **Method Signatures**: Must match actual Cashu plugin implementation
4. **Token Format**: Must match Cashu specification exactly

## Next Steps for Implementation

1. **Start with Service Discovery**:
   - Enhance `TryDiscoverCashuService()` to find wallet services
   - Add logging for all discovered services
   - Test service discovery with actual Cashu plugin

2. **Implement Swap Logic**:
   - Discover Swap method on wallet service
   - Implement swap operation
   - Test swap with actual wallet balance

3. **Add Token Creation**:
   - Implement proof to token conversion
   - Test token format
   - Validate token with Cashu specification

4. **Test and Refine**:
   - Test with real rewards
   - Refine error handling
   - Add comprehensive logging

## References

- [Cashu Development Kit (CDK)](https://github.com/cashubtc/cdk)
- [Cashu NUT Specifications](https://github.com/cashubtc/nuts)
- [NUT-03: Swapping Tokens](https://github.com/cashubtc/nuts/blob/main/03.md)
- [NUT-04: Minting Tokens](https://github.com/cashubtc/nuts/blob/main/04.md)

## Current Code Context

### Error Message
```
fail: BTCPayServer.Plugins.BitcoinRewards.Services.CashuServiceAdapter: Minting method not found on Cashu service. Available methods: ProcessPaymentAsync, RegisterCashuPayment, AddProofsToDb, PollFailedMelt, PollFailedSwap
```

### Service Discovery Result
- Service found: `CashuPaymentService`
- Service type: Payment processing service
- Methods available: Payment-related only
- Missing: Token minting/creation methods

### Wallet Status
- ✅ Wallet installed and active
- ✅ Wallet has balance: 88 sat
- ✅ Trusted mints configured: 1 mint
- ❌ Wallet service not discovered for token creation

## Success Metrics

After implementation, we should see:

1. **Service Discovery**: Wallet service found with Swap methods
2. **Token Creation**: Tokens created successfully from wallet proofs
3. **Error Reduction**: No more "Minting method not found" errors
4. **Logging**: Detailed logs showing swap operations and token creation
5. **Reward Processing**: Rewards successfully created with valid tokens

## Implementation Priority

**Critical Path**:
1. Enhance service discovery (Step 1)
2. Implement swap-based token creation (Step 2)
3. Add token creation logic (Step 3)

**Important but not blocking**:
4. Enhanced error handling (Step 4)
5. Comprehensive testing (Step 5)

---

**Document Version**: 1.0  
**Last Updated**: Current session  
**Status**: Ready for implementation  
**Next Agent**: Begin with Step 1 (Enhanced Service Discovery)
