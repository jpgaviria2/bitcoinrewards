# Cashu Token Minting Implementation Checklist

## Pre-Implementation Setup

- [ ] Review current `CashuServiceAdapter.cs` code
- [ ] Understand Cashu CDK NUT-03 (swapping tokens) specification
- [ ] Review existing service discovery logic
- [ ] Check Cashu plugin documentation (if available)
- [ ] Set up testing environment with Cashu wallet

## Step 1: Enhanced Service Discovery

### Code Changes
- [ ] Add `_cashuWalletService` field to store wallet service separately
- [ ] Modify `TryDiscoverCashuService()` to prioritize wallet services
- [ ] Add method to check for Swap methods on discovered services
- [ ] Store wallet service reference separately from payment service
- [ ] Add detailed logging for all discovered services

### Service Discovery Logic
- [ ] Search for services with "Wallet" in name first
- [ ] Check each service for Swap methods using reflection
- [ ] Prioritize services with Swap methods
- [ ] Log all discovered services with their full type names
- [ ] Log all available methods on each service

### Testing
- [ ] Verify wallet service is discovered (if available)
- [ ] Verify payment service is still discovered as fallback
- [ ] Check logs show all discovered services and methods
- [ ] Verify service discovery works on startup

## Step 2: Implement Swap-Based Token Creation

### Code Changes
- [ ] Rewrite `MintTokenAsync()` to use wallet service
- [ ] Add `DiscoverWalletService()` helper method
- [ ] Add `FindSwapMethod()` helper method
- [ ] Add `SwapProofsAsync()` method to execute swap
- [ ] Update error handling for wallet-specific errors

### Swap Method Discovery
- [ ] Try multiple Swap method signatures:
  - [ ] `SwapAsync(ulong amount, string mintUrl)`
  - [ ] `SwapAsync(long amountSatoshis, string storeId)`
  - [ ] `Swap(ulong amount)`
  - [ ] Other variations with "Swap" in name
- [ ] Handle different return types (proofs, proof list, etc.)
- [ ] Log swap method discovery process

### Swap Operation
- [ ] Call Swap method with reward amount in satoshis
- [ ] Handle async Task return types
- [ ] Extract proofs from swap result
- [ ] Handle errors during swap operation
- [ ] Log swap operation results

### Testing
- [ ] Test Swap method discovery
- [ ] Test swap operation with actual wallet
- [ ] Test swap with different amounts
- [ ] Test swap error handling (insufficient balance, etc.)

## Step 3: Add Token Creation Logic

### Code Changes
- [ ] Add `CreateTokenFromProofs()` helper method
- [ ] Implement proof to token conversion
- [ ] Create token JSON structure with proofs
- [ ] Include mint URL and metadata in token
- [ ] Base64 encode the JSON
- [ ] Format as Cashu token string ("cashuAeyJ...")

### Token Format
- [ ] Ensure token follows Cashu specification
- [ ] Include all required token fields
- [ ] Validate token structure
- [ ] Test token encoding/decoding

### Testing
- [ ] Test token creation from proofs
- [ ] Verify token format matches Cashu spec
- [ ] Test token can be decoded
- [ ] Validate token with Cashu clients

## Step 4: Enhanced Error Handling and Logging

### Logging Improvements
- [ ] Log service discovery process in detail
- [ ] Log all discovered services with types
- [ ] Log all available methods on each service
- [ ] Log Swap method discovery attempts
- [ ] Log swap operation parameters and results
- [ ] Log token creation steps
- [ ] Log errors with full context

### Error Handling
- [ ] Handle "wallet service not found" error
- [ ] Handle "Swap method not found" error
- [ ] Handle "insufficient balance" error
- [ ] Handle "swap operation failed" error
- [ ] Handle "token creation failed" error
- [ ] Provide clear, actionable error messages

### Testing
- [ ] Test error scenarios
- [ ] Verify error messages are clear
- [ ] Check logs contain sufficient debugging info

## Step 5: Testing and Validation

### Unit Testing
- [ ] Test service discovery with mock services
- [ ] Test Swap method discovery
- [ ] Test swap operation with mock data
- [ ] Test token creation logic

### Integration Testing
- [ ] Test with actual Cashu wallet
- [ ] Create real reward and verify token creation
- [ ] Verify token format is valid
- [ ] Test with different reward amounts
- [ ] Test error scenarios with real wallet

### End-to-End Testing
- [ ] Create test reward through UI
- [ ] Verify token is created successfully
- [ ] Verify token is sent to customer
- [ ] Verify token can be redeemed

### Manual Testing
- [ ] Test with real Cashu wallet
- [ ] Create real reward
- [ ] Verify token works with Cashu clients
- [ ] Test edge cases (large amounts, small amounts, etc.)

## Code Quality

- [ ] Code follows project coding standards
- [ ] Code is well-commented
- [ ] Error handling is comprehensive
- [ ] Logging is detailed and useful
- [ ] No hardcoded values (use constants/config)
- [ ] Code is maintainable and readable

## Documentation

- [ ] Code comments explain complex logic
- [ ] Method documentation is complete
- [ ] Error scenarios are documented
- [ ] Token format is documented
- [ ] Service discovery process is documented

## CDK Alignment Verification

- [ ] Implementation uses NUT-03 (swapping tokens)
- [ ] Implementation works with existing wallet proofs
- [ ] Implementation follows wallet service patterns
- [ ] Token format matches Cashu specification
- [ ] Error handling follows CDK best practices

## Final Verification

- [ ] All tests pass
- [ ] No "Minting method not found" errors
- [ ] Tokens created successfully
- [ ] Token format is valid
- [ ] Logs are detailed and helpful
- [ ] Code is ready for production

---

**Checklist Version**: 1.0  
**Use this checklist**: Follow step-by-step, checking off items as you complete them  
**Priority**: Complete Steps 1-3 first (critical path), then Steps 4-5

