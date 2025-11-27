# Bitcoin Rewards Plugin - Production Readiness Plan

**Date:** January 27, 2025  
**Status:** Development Phase - Ready for Testing & Quality Assurance  
**Next Phase:** Unit Testing, Code Quality Verification, and Production Hardening

---

## 📋 Executive Summary

This document outlines the work completed today and provides a comprehensive plan for bringing the Bitcoin Rewards plugin to production readiness. The plugin now has a fully functional independent Cashu wallet system with UI, configuration, and top-up capabilities. The next phase focuses on comprehensive testing, code quality improvements, and handling edge cases.

---

## ✅ Work Completed Today

### 1. Independent Cashu Wallet Implementation

**What Was Built:**
- **InternalCashuWallet**: Direct integration with DotNut library for Cashu operations
- **ProofStorageService**: Database service for managing Cashu proofs independently
- **WalletConfigurationService**: Service for managing mint URL configuration per store
- **WalletController**: Full MVC controller with Index, Configure, and TopUp actions
- **Wallet Views**: Complete UI for wallet management (Index.cshtml, Configure.cshtml, TopUp.cshtml)
- **ViewModels**: WalletViewModel, WalletConfigurationViewModel, TopUpViewModel

**Key Features:**
- Independent wallet that doesn't rely on Cashu plugin
- Per-store mint URL configuration
- Lightning-to-ecash top-up functionality
- Balance display (ecash and Lightning)
- Navigation integration in BTCPay Server UI

**Files Created/Modified:**
- `Services/InternalCashuWallet.cs` (NEW)
- `Services/ProofStorageService.cs` (NEW)
- `Services/WalletConfigurationService.cs` (NEW)
- `Controllers/WalletController.cs` (NEW)
- `Views/Wallet/*.cshtml` (NEW - 3 files)
- `ViewModels/Wallet*.cs` (NEW - 3 files)
- `Data/Models/StoredProof.cs` (NEW)
- `Data/Models/Mint.cs` (NEW)
- `Data/Migrations/20250127000000_AddProofsAndMints.cs` (NEW)
- `Data/BitcoinRewardsPluginDbContext.cs` (MODIFIED - added DbSets)
- `Services/CashuServiceAdapter.cs` (REFACTORED - now uses internal wallet)
- `BitcoinRewardsPlugin.cs` (MODIFIED - registered new services and UI extensions)

### 2. Cashu Service Refactoring

**Changes:**
- `CashuServiceAdapter` now primarily uses `InternalCashuWallet` instead of reflection
- Retains optional fallback to Cashu plugin for compatibility
- `GetEcashBalanceAsync` now uses `ProofStorageService` directly
- `SwapProofsAsync` and `MintFromLightningAsync` use internal wallet
- Added `GetLightningBalanceAsync` to `ICashuService` interface

**Benefits:**
- More reliable (less reflection-based code)
- Better error handling
- Independent operation
- Easier to test and maintain

### 3. UI Enhancements

**Navigation:**
- Added wallet link to header navigation (`BitcoinRewardsNavExtension.cshtml`)
- Added wallet link to store wallets section (`WalletNavExtension.cshtml`)
- Custom star icon for plugin branding

**User Experience:**
- Wallet automatically redirects to Configure if not set up
- Clear error messages and status notifications
- Intuitive top-up flow with Lightning integration

### 4. Database Schema

**New Tables:**
- `Proofs`: Stores Cashu proofs per store and mint URL
- `Mints`: Stores configured mint URLs per store

**Migration:**
- `20250127000000_AddProofsAndMints.cs` - Creates new tables in plugin schema

---

## 🔍 Current State Assessment

### ✅ What's Working

1. **Core Functionality:**
   - Independent Cashu wallet with database storage
   - Mint URL configuration per store
   - Lightning balance retrieval
   - Ecash balance calculation from stored proofs
   - Wallet UI accessible via navigation

2. **Integration:**
   - BTCPay Server plugin architecture
   - DotNut library for Cashu operations
   - Lightning client factory integration
   - Database migrations working

3. **Code Structure:**
   - Clean separation of concerns
   - Dependency injection properly configured
   - Services are testable (though not yet tested)

### ⚠️ Known Issues & Limitations

1. **Testing:**
   - No unit tests exist
   - No integration tests
   - Manual testing only

2. **Error Handling:**
   - Some edge cases not handled
   - Limited validation in some areas
   - Error messages could be more user-friendly

3. **Code Quality:**
   - Some reflection-based code still present (for Cashu plugin fallback)
   - Nullable reference types warnings may exist
   - Missing XML documentation on some methods

4. **Missing Features:**
   - No wallet export functionality
   - No proof history/audit trail
   - No mint health checking
   - No automatic retry logic for failed operations

---

## 🎯 Production Readiness Goals

### Phase 1: Unit Testing (Priority: HIGH)

**Goal:** Achieve >80% code coverage with comprehensive unit tests

#### 1.1 Test Project Setup

**Tasks:**
- [ ] Create `BTCPayServer.Plugins.BitcoinRewards.Tests` project
- [ ] Add xUnit, Moq, FluentAssertions packages
- [ ] Set up test project structure (mirror main project)
- [ ] Configure test project to reference main project
- [ ] Add test data builders/helpers

**Files to Create:**
```
Plugins/BTCPayServer.Plugins.BitcoinRewards.Tests/
├── BTCPayServer.Plugins.BitcoinRewards.Tests.csproj
├── Services/
│   ├── ProofStorageServiceTests.cs
│   ├── WalletConfigurationServiceTests.cs
│   ├── InternalCashuWalletTests.cs
│   ├── CashuServiceAdapterTests.cs
│   └── BitcoinRewardsServiceTests.cs
├── Controllers/
│   ├── WalletControllerTests.cs
│   └── UIBitcoinRewardsControllerTests.cs
├── Data/
│   └── BitcoinRewardsPluginDbContextTests.cs
└── Helpers/
    ├── TestDataBuilder.cs
    └── MockHelpers.cs
```

#### 1.2 Service Layer Tests

**ProofStorageService Tests:**
- [ ] `AddProofsAsync` - Success case
- [ ] `AddProofsAsync` - Null/empty proofs
- [ ] `AddProofsAsync` - Database errors
- [ ] `GetProofsAsync` - Filter by store
- [ ] `GetProofsAsync` - Filter by mint URL
- [ ] `GetProofsAsync` - Empty results
- [ ] `RemoveProofsAsync` - Success case
- [ ] `RemoveProofsAsync` - Non-existent proofs
- [ ] `GetBalanceAsync` - Single store
- [ ] `GetBalanceAsync` - Multiple mints
- [ ] `GetBalanceAsync` - Zero balance
- [ ] `GetMintsAsync` - Active mints only
- [ ] `GetMintAsync` - By store and URL
- [ ] `AddMintAsync` - New mint
- [ ] `AddMintAsync` - Duplicate mint URL
- [ ] `UpdateMintAsync` - Deactivate existing
- [ ] `GetActiveMintAsync` - Single active mint

**WalletConfigurationService Tests:**
- [ ] `GetMintUrlAsync` - From database
- [ ] `GetMintUrlAsync` - No mint configured
- [ ] `GetMintUrlAsync` - Fallback to Cashu plugin (if available)
- [ ] `SetMintUrlAsync` - New mint
- [ ] `SetMintUrlAsync` - Update existing mint
- [ ] `SetMintUrlAsync` - Invalid URL
- [ ] `SetMintUrlAsync` - Empty/null URL
- [ ] `GetConfigurationAsync` - With balance
- [ ] `GetConfigurationAsync` - No configuration
- [ ] `GetConfigurationAsync` - Multiple stores isolation

**InternalCashuWallet Tests:**
- [ ] `GetEcashBalance` - With proofs
- [ ] `GetEcashBalance` - Empty wallet
- [ ] `GetStoredProofs` - Filter by keyset
- [ ] `Swap` - Success case
- [ ] `Swap` - Insufficient balance
- [ ] `Swap` - Invalid keyset
- [ ] `Swap` - Network errors
- [ ] `MintFromLightning` - Success case
- [ ] `MintFromLightning` - Invalid amount
- [ ] `MintFromLightning` - Lightning payment failure
- [ ] `MintFromLightning` - Mint quote timeout
- [ ] `GetActiveKeyset` - Success
- [ ] `GetActiveKeyset` - No keyset available
- [ ] `GetKeys` - Valid keyset
- [ ] `GetKeys` - Invalid keyset ID
- [ ] `CreateOutputs` - Various amounts
- [ ] `CreateProofs` - Valid outputs

**CashuServiceAdapter Tests:**
- [ ] `MintTokenAsync` - Sufficient ecash balance (swap)
- [ ] `MintTokenAsync` - Insufficient ecash, sufficient Lightning (mint)
- [ ] `MintTokenAsync` - Insufficient both (error)
- [ ] `MintTokenAsync` - No mint URL configured
- [ ] `MintTokenAsync` - Network errors
- [ ] `GetEcashBalanceAsync` - With proofs
- [ ] `GetEcashBalanceAsync` - Empty
- [ ] `GetLightningBalanceAsync` - Success
- [ ] `GetLightningBalanceAsync` - No Lightning configured
- [ ] `GetLightningBalanceAsync` - Service unavailable
- [ ] `GetMintUrlForStore` - From wallet config
- [ ] `GetMintUrlForStore` - Fallback to Cashu plugin
- [ ] `GetMintUrlForStore` - Not found
- [ ] `SwapProofsAsync` - Success
- [ ] `SwapProofsAsync` - Insufficient balance
- [ ] `MintFromLightningAsync` - Success
- [ ] `MintFromLightningAsync` - Payment timeout
- [ ] `ReclaimTokenAsync` - Valid token
- [ ] `ReclaimTokenAsync` - Already claimed
- [ ] `ValidateTokenAsync` - Valid token
- [ ] `ValidateTokenAsync` - Invalid token

**BitcoinRewardsService Tests:**
- [ ] `ProcessRewardAsync` - Happy path
- [ ] `ProcessRewardAsync` - Plugin disabled
- [ ] `ProcessRewardAsync` - Below minimum amount
- [ ] `ProcessRewardAsync` - Duplicate transaction
- [ ] `ProcessRewardAsync` - Currency conversion
- [ ] `ProcessRewardAsync` - Email sending failure
- [ ] `ProcessRewardAsync` - Cashu minting failure

#### 1.3 Controller Tests

**WalletController Tests:**
- [ ] `Index` - With configuration
- [ ] `Index` - Without configuration (redirects)
- [ ] `Index` - Store not found
- [ ] `Configure GET` - New configuration
- [ ] `Configure GET` - Existing configuration
- [ ] `Configure POST` - Valid mint URL
- [ ] `Configure POST` - Invalid mint URL
- [ ] `Configure POST` - Empty mint URL
- [ ] `TopUp GET` - With configuration
- [ ] `TopUp GET` - Without configuration (redirects)
- [ ] `TopUpFromLightning POST` - Valid amount
- [ ] `TopUpFromLightning POST` - Zero amount
- [ ] `TopUpFromLightning POST` - Minting failure
- [ ] Authorization checks (CanModifyStoreSettings)

**UIBitcoinRewardsController Tests:**
- [ ] `EditSettings GET` - Load existing settings
- [ ] `EditSettings POST` - Save valid settings
- [ ] `EditSettings POST` - Validation errors
- [ ] `TestReward` - Success case
- [ ] `TestReward` - Minting failure
- [ ] Authorization checks

#### 1.4 Database Tests

**BitcoinRewardsPluginDbContext Tests:**
- [ ] Migration execution
- [ ] Table creation
- [ ] Index creation
- [ ] Foreign key constraints
- [ ] Data isolation between stores

### Phase 2: Integration Testing (Priority: MEDIUM)

**Goal:** Test real-world scenarios with actual dependencies

#### 2.1 Test Environment Setup

**Tasks:**
- [ ] Create integration test project
- [ ] Set up in-memory database for tests
- [ ] Mock external services (Lightning, Cashu mint)
- [ ] Create test fixtures for common scenarios

#### 2.2 Integration Test Scenarios

**Wallet Operations:**
- [ ] End-to-end: Configure mint → Top up → Check balance
- [ ] End-to-end: Top up → Swap proofs → Mint token
- [ ] Concurrent operations (multiple stores)
- [ ] Database transaction rollback on errors

**Reward Processing:**
- [ ] End-to-end: Webhook → Process reward → Send email
- [ ] End-to-end: Multiple rewards in sequence
- [ ] Error recovery (retry failed operations)

### Phase 3: Code Quality Improvements (Priority: HIGH)

#### 3.1 Static Analysis

**Tasks:**
- [ ] Enable nullable reference types warnings as errors
- [ ] Fix all nullable warnings
- [ ] Add XML documentation to all public APIs
- [ ] Run code analysis tools (SonarAnalyzer, StyleCop)
- [ ] Fix code style violations
- [ ] Remove unused code
- [ ] Refactor complex methods

**Tools to Use:**
- `dotnet format` for code formatting
- `Microsoft.CodeAnalysis.NetAnalyzers` for code analysis
- `StyleCop.Analyzers` for style rules
- `Nullable` reference types for null safety

#### 3.2 Error Handling

**Improvements Needed:**
- [ ] Add try-catch blocks with proper logging
- [ ] Create custom exception types for domain errors
- [ ] Add retry logic for transient failures
- [ ] Improve error messages for users
- [ ] Add error codes for troubleshooting
- [ ] Handle network timeouts gracefully
- [ ] Handle database connection failures

**Exception Types to Create:**
```csharp
- MintConfigurationException
- InsufficientBalanceException
- LightningPaymentException
- CashuMintException
- ProofStorageException
```

#### 3.3 Logging

**Improvements:**
- [ ] Add structured logging throughout
- [ ] Use appropriate log levels (Debug, Info, Warning, Error)
- [ ] Add correlation IDs for request tracking
- [ ] Log all external API calls
- [ ] Log all database operations (at Debug level)
- [ ] Add performance logging for slow operations

#### 3.4 Validation

**Add Validation:**
- [ ] Input validation in controllers
- [ ] Business rule validation in services
- [ ] Mint URL format validation
- [ ] Amount validation (positive, reasonable limits)
- [ ] Store ID validation
- [ ] Email address validation (if applicable)

### Phase 4: Corner Cases & Edge Cases (Priority: HIGH)

#### 4.1 Wallet Operations

**Scenarios to Test:**
- [ ] **Concurrent Top-Ups**: Multiple top-up requests simultaneously
- [ ] **Network Partitions**: Mint server unreachable during operations
- [ ] **Partial Failures**: Lightning payment succeeds but mint fails
- [ ] **Race Conditions**: Balance check vs. swap operation
- [ ] **Large Amounts**: Top-up with very large amounts (overflow checks)
- [ ] **Zero Amounts**: Attempting to top up with 0 sat
- [ ] **Negative Amounts**: Invalid input handling
- [ ] **Mint URL Changes**: Changing mint URL while operations in progress
- [ ] **Database Locking**: Concurrent database operations
- [ ] **Proof Corruption**: Invalid proof data in database
- [ ] **Mint Key Rotation**: Mint changes keysets during operation
- [ ] **Lightning Node Offline**: Lightning node unavailable
- [ ] **Insufficient Lightning Balance**: Top-up exceeds available balance
- [ ] **Mint Quote Expiry**: Quote expires before payment completes
- [ ] **Payment Timeout**: Lightning payment takes too long

#### 4.2 Reward Processing

**Scenarios to Test:**
- [ ] **Duplicate Transactions**: Same transaction processed twice
- [ ] **Very Small Rewards**: Rewards below minimum threshold
- [ ] **Very Large Rewards**: Rewards exceeding maximum cap
- [ ] **Currency Conversion Failures**: Rate service unavailable
- [ ] **Invalid Currency Codes**: Unsupported currencies
- [ ] **Missing Customer Email**: No email for delivery
- [ ] **Email Service Failure**: Email plugin not available
- [ ] **Plugin Disabled Mid-Processing**: Plugin disabled during reward processing
- [ ] **Store Deletion**: Store deleted while rewards pending
- [ ] **Concurrent Webhooks**: Multiple webhooks for same transaction
- [ ] **Webhook Replay**: Old webhook received again
- [ ] **Invalid Webhook Data**: Malformed webhook payloads

#### 4.3 Configuration

**Scenarios to Test:**
- [ ] **Invalid Mint URLs**: Malformed URLs, unreachable mints
- [ ] **Mint URL Changes**: Changing mint URL with existing proofs
- [ ] **Multiple Active Mints**: Edge case handling
- [ ] **Store Isolation**: Configuration doesn't leak between stores
- [ ] **Configuration Deletion**: Removing configuration with active proofs
- [ ] **Migration Scenarios**: Upgrading plugin with existing data

#### 4.4 Database

**Scenarios to Test:**
- [ ] **Migration Failures**: Handling failed migrations
- [ ] **Schema Conflicts**: Conflicts with other plugins
- [ ] **Large Datasets**: Performance with many proofs/rewards
- [ ] **Data Integrity**: Foreign key violations
- [ ] **Transaction Rollbacks**: Partial operation failures
- [ ] **Connection Pooling**: High concurrency scenarios

### Phase 5: Performance & Scalability (Priority: MEDIUM)

#### 5.1 Performance Testing

**Areas to Test:**
- [ ] Database query performance (indexes)
- [ ] Large proof sets (1000+ proofs)
- [ ] Concurrent wallet operations
- [ ] Webhook processing throughput
- [ ] Memory usage with large datasets
- [ ] Response times for UI operations

#### 5.2 Optimization Opportunities

**Potential Improvements:**
- [ ] Add database indexes if missing
- [ ] Implement caching for mint keysets
- [ ] Batch database operations where possible
- [ ] Optimize proof queries
- [ ] Add pagination for large result sets
- [ ] Consider async/await optimization

### Phase 6: Security Hardening (Priority: HIGH)

#### 6.1 Security Review

**Areas to Review:**
- [ ] **Input Sanitization**: All user inputs validated and sanitized
- [ ] **SQL Injection**: Parameterized queries only
- [ ] **Authorization**: All endpoints properly authorized
- [ ] **Store Isolation**: Data properly isolated between stores
- [ ] **Secret Management**: No secrets in code/logs
- [ ] **HTTPS Only**: All external API calls use HTTPS
- [ ] **Rate Limiting**: Prevent abuse of endpoints
- [ ] **CSRF Protection**: Forms protected against CSRF

#### 6.2 Security Tests

**Tests to Add:**
- [ ] Unauthorized access attempts
- [ ] Cross-store data access attempts
- [ ] SQL injection attempts
- [ ] XSS attempts in user inputs
- [ ] CSRF token validation

### Phase 7: Documentation (Priority: MEDIUM)

#### 7.1 Code Documentation

**Tasks:**
- [ ] Add XML documentation to all public APIs
- [ ] Document complex algorithms
- [ ] Add inline comments for non-obvious code
- [ ] Document error codes and exceptions

#### 7.2 User Documentation

**Tasks:**
- [ ] Update README with wallet setup instructions
- [ ] Create user guide for wallet operations
- [ ] Document configuration options
- [ ] Create troubleshooting guide
- [ ] Add FAQ section

#### 7.3 Developer Documentation

**Tasks:**
- [ ] Architecture overview
- [ ] Service dependencies diagram
- [ ] Database schema documentation
- [ ] API documentation
- [ ] Contributing guidelines

---

## 🐛 Known Issues to Address

### Critical Issues

1. **No Error Recovery for Mint Operations**
   - **Issue**: If Lightning payment succeeds but mint fails, funds could be lost
   - **Fix**: Implement compensation logic or rollback mechanism
   - **Priority**: HIGH

2. **Race Conditions in Balance Checks**
   - **Issue**: Balance checked, then used, but could change in between
   - **Fix**: Use database transactions with proper isolation levels
   - **Priority**: HIGH

3. **Missing Validation on Mint URLs**
   - **Issue**: Invalid mint URLs can be configured
   - **Fix**: Add URL validation and connectivity check
   - **Priority**: MEDIUM

### Medium Priority Issues

4. **Limited Error Messages**
   - **Issue**: Generic error messages don't help users troubleshoot
   - **Fix**: Add specific, actionable error messages
   - **Priority**: MEDIUM

5. **No Proof Audit Trail**
   - **Issue**: Can't track proof history or operations
   - **Fix**: Add audit logging for proof operations
   - **Priority**: LOW

6. **Reflection-Based Code**
   - **Issue**: Some reflection code for Cashu plugin fallback
   - **Fix**: Consider removing or making more robust
   - **Priority**: LOW

---

## 📊 Testing Metrics & Goals

### Code Coverage Goals

- **Overall Coverage**: >80%
- **Service Layer**: >90%
- **Controllers**: >85%
- **Critical Paths**: 100%

### Quality Metrics

- **Static Analysis**: Zero critical/high severity issues
- **Code Smells**: <10
- **Technical Debt**: <5% of codebase
- **Cyclomatic Complexity**: <10 per method

### Performance Targets

- **Wallet Operations**: <2 seconds
- **Top-Up Operations**: <30 seconds (including Lightning payment)
- **Database Queries**: <100ms for typical queries
- **UI Page Load**: <1 second

---

## 🚀 Implementation Roadmap

### Week 1: Foundation
- [ ] Set up test projects
- [ ] Create test infrastructure (mocks, builders)
- [ ] Write tests for ProofStorageService
- [ ] Write tests for WalletConfigurationService

### Week 2: Core Services
- [ ] Write tests for InternalCashuWallet
- [ ] Write tests for CashuServiceAdapter
- [ ] Write tests for BitcoinRewardsService
- [ ] Fix issues found during testing

### Week 3: Controllers & Integration
- [ ] Write controller tests
- [ ] Write integration tests
- [ ] Fix edge cases discovered
- [ ] Improve error handling

### Week 4: Quality & Hardening
- [ ] Code quality improvements
- [ ] Security review
- [ ] Performance optimization
- [ ] Documentation updates

### Week 5: Final Polish
- [ ] Final testing pass
- [ ] Bug fixes
- [ ] Documentation completion
- [ ] Release preparation

---

## 📝 Next Steps for Next Agent

### Immediate Actions (Day 1)

1. **Review This Document**
   - Understand current state
   - Review completed work
   - Identify priority areas

2. **Set Up Test Environment**
   - Create test project
   - Install testing packages
   - Set up test infrastructure

3. **Start with ProofStorageService Tests**
   - Simplest service to test
   - Good starting point
   - Builds testing patterns

### Recommended Approach

1. **Start Small**: Begin with unit tests for simplest services
2. **Build Up**: Progress to more complex services
3. **Test-Driven**: Write tests first, then fix issues found
4. **Iterate**: Test → Fix → Test → Fix cycle
5. **Document**: Document issues and solutions as you go

### Key Files to Focus On

**High Priority (Test First):**
- `Services/ProofStorageService.cs`
- `Services/WalletConfigurationService.cs`
- `Services/InternalCashuWallet.cs`
- `Services/CashuServiceAdapter.cs`

**Medium Priority:**
- `Controllers/WalletController.cs`
- `Services/BitcoinRewardsService.cs`
- `Controllers/UIBitcoinRewardsController.cs`

**Lower Priority:**
- View models
- Data models
- Helpers/utilities

---

## 🔗 Related Documents

- `TESTING_CHECKLIST.md` - Original testing checklist (may be outdated)
- `CASHU_MINTING_IMPLEMENTATION_SUMMARY.md` - Cashu integration details
- `IMPLEMENTATION_CHECKLIST.md` - Implementation checklist
- `BUILD_INSTRUCTIONS.md` - Build and deployment instructions

---

## 📞 Questions & Support

If you encounter issues or need clarification:

1. **Review Code Comments**: Check inline documentation
2. **Check Git History**: Review commit messages for context
3. **Test Existing Functionality**: Run the plugin to understand behavior
4. **Review BTCPay Server Docs**: Understand plugin architecture

---

## ✅ Definition of Done

The plugin is production-ready when:

- [ ] All unit tests pass (>80% coverage)
- [ ] All integration tests pass
- [ ] Zero critical/high severity static analysis issues
- [ ] All edge cases handled
- [ ] Error handling comprehensive
- [ ] Logging adequate for troubleshooting
- [ ] Documentation complete
- [ ] Security review passed
- [ ] Performance targets met
- [ ] Manual testing completed
- [ ] Code review approved

---

**Last Updated:** January 27, 2025  
**Next Review:** After test implementation phase

