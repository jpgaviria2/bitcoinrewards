# Cashu Token Minting - Quick Reference Guide

## Problem Statement

**Error**: `Minting method not found on Cashu service`  
**Root Cause**: `CashuPaymentService` doesn't have token minting methods  
**Solution**: Use wallet service with Swap method to split proofs and create tokens

## Key Files

- **Primary**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/CashuServiceAdapter.cs`
- **Consumer**: `Plugins/BTCPayServer.Plugins.BitcoinRewards/Services/BitcoinRewardsService.cs` (line 144)

## Current Issue

```csharp
// Current: Looks for minting methods on CashuPaymentService
// Problem: CashuPaymentService only has: ProcessPaymentAsync, RegisterCashuPayment, etc.
// Missing: Methods to mint/create tokens
```

## Solution Strategy

### CDK-Aligned Approach (NUT-03)

1. **Discover Wallet Service** (not payment service)
2. **Use Swap Method** to split existing proofs
3. **Create Token** from swapped proofs
4. **Return Token** for reward delivery

### Key Concept

- **Wallet**: Manages proofs, can swap/split them → Use this!
- **Payment Service**: Processes payments → Not suitable for minting
- **NUT-03**: Swapping tokens (split proofs) → What we need
- **NUT-04**: Minting from Lightning → Not needed (we have proofs)

## Implementation Steps

### 1. Enhance Service Discovery

```csharp
// Add wallet service field
private object? _cashuWalletService;

// In TryDiscoverCashuService():
// - Look for services with "Wallet" in name
// - Check for Swap methods
// - Prioritize wallet services
// - Store wallet service separately
```

### 2. Implement Swap-Based Minting

```csharp
// In MintTokenAsync():
// 1. Use _cashuWalletService (not _cashuService)
// 2. Find Swap method using reflection
// 3. Call Swap(amountSatoshis) to get proofs
// 4. Convert proofs to token format
// 5. Return token string
```

### 3. Token Creation

```csharp
// Create token from proofs:
// - Create JSON with proofs, mint URL, metadata
// - Base64 encode JSON
// - Format as "cashuAeyJ..." string
```

## Method Discovery Patterns

```csharp
// Try these Swap method signatures:
SwapAsync(ulong amount, string mintUrl)
SwapAsync(long amountSatoshis, string storeId)
Swap(ulong amount)
SwapProofs(ulong amount)

// Check for methods with "Swap" in name
// Handle different return types (proofs, proof list, etc.)
```

## Logging Examples

### Success Flow
```
INFO: Cashu wallet service found: CashuWalletService
INFO: Wallet service has SwapAsync method
INFO: Swapping proofs for 1000 sats
INFO: Swap successful, received 2 proofs
INFO: Token created: cashuAeyJ...
```

### Error Flow
```
WARNING: Wallet service not found
INFO: Discovered services: CashuPaymentService
ERROR: Cannot mint tokens without wallet service

WARNING: Swap method not found
INFO: Available methods: GetBalance, GetProofs, ...
ERROR: Cannot swap proofs without Swap method
```

## Code Snippets

### Service Discovery Enhancement

```csharp
// Look for wallet services first
var walletServices = allTypes
    .Where(t => t.Name.Contains("Wallet") && !t.IsAbstract)
    .ToList();

foreach (var walletType in walletServices) {
    var service = _serviceProvider.GetService(walletType);
    if (service != null) {
        // Check for Swap method
        var hasSwap = walletType.GetMethods()
            .Any(m => m.Name.Contains("Swap"));
        if (hasSwap) {
            _cashuWalletService = service;
            break;
        }
    }
}
```

### Swap Method Discovery

```csharp
var swapMethods = new[] {
    ("SwapAsync", new[] { typeof(ulong), typeof(string) }),
    ("SwapAsync", new[] { typeof(long), typeof(string) }),
    ("Swap", new[] { typeof(ulong) }),
};

MethodInfo? swapMethod = null;
foreach (var (methodName, paramTypes) in swapMethods) {
    swapMethod = _cashuWalletService.GetType()
        .GetMethod(methodName, paramTypes);
    if (swapMethod != null) break;
}
```

### Swap Operation

```csharp
// Call swap method
var result = swapMethod.Invoke(_cashuWalletService, 
    new object[] { amountSatoshis, storeId });

// Handle async result
if (result is Task<object> task) {
    var proofs = await task;
    // Convert proofs to token
}
```

## Testing Checklist

- [ ] Wallet service discovered
- [ ] Swap method found
- [ ] Swap operation succeeds
- [ ] Token created successfully
- [ ] Token format is valid
- [ ] Token can be decoded

## Common Errors & Solutions

| Error | Solution |
|-------|----------|
| "Minting method not found" | Use wallet service with Swap method |
| "Wallet service not found" | Enhance service discovery to find wallet services |
| "Swap method not found" | Check all methods, look for "Swap" in name |
| "Insufficient balance" | Check wallet balance before swapping |

## References

- **CDK**: https://github.com/cashubtc/cdk
- **NUT-03**: https://github.com/cashubtc/nuts/blob/main/03.md
- **Full Summary**: See `CASHU_MINTING_IMPLEMENTATION_SUMMARY.md`
- **Checklist**: See `IMPLEMENTATION_CHECKLIST.md`

## Quick Start

1. Read `CASHU_MINTING_IMPLEMENTATION_SUMMARY.md` for full context
2. Follow `IMPLEMENTATION_CHECKLIST.md` step-by-step
3. Use this quick reference for code patterns
4. Test with actual Cashu wallet
5. Verify token creation works

---

**Status**: Ready for implementation  
**Priority**: Critical - blocks reward creation  
**Estimated Time**: 2-4 hours for full implementation
