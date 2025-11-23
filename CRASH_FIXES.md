# Plugin Crash Fixes and Testing Setup

## Summary

This document describes the fixes applied to prevent the plugin from crashing the server and the testing infrastructure created to identify issues.

## Issues Fixed

### 1. Plugin Initialization Crashes

**Problem**: Plugin was crashing during initialization when required BTCPay Server services were not available.

**Fixes Applied**:

1. **Added Try-Catch in Execute Method**
   - Wrapped plugin initialization in try-catch
   - Provides clear error messages instead of silent crashes
   - Allows BTCPay Server to handle plugin loading errors gracefully

2. **Defensive Service Resolution**
   - Changed `GetRequiredService` to `GetService` with fallback
   - Added null checks before using services
   - Throws `InvalidOperationException` with clear messages when required services are missing

3. **RateService Null Safety**
   - Added null checks for `RateProviderFactory.Providers`
   - Added null-conditional operators for logging
   - Handles cases where rate providers are not configured

4. **Service Registration Validation**
   - Validates all required services are available before registration
   - Provides specific error messages for missing dependencies
   - Prevents silent failures

### 2. Missing Dependency Handling

**Problem**: Plugin would crash if optional dependencies (like Emails plugin) were not available.

**Fixes Applied**:

1. **Optional Email Service**
   - Already handled with reflection-based loading
   - Enhanced error handling in EmailService registration

2. **RateProviderFactory Validation**
   - Checks if RateProviderFactory is available before registering RateService
   - Throws clear error if required service is missing

3. **EventAggregator Validation**
   - Validates EventAggregator is available before registering BitcoinRewardsService
   - Prevents crashes when hosted services can't be initialized

## Testing Infrastructure

### Unit Test Project

Created `BTCPayServer.Plugins.BitcoinRewards.Tests` with:

- **xUnit** for test framework
- **Moq** for mocking dependencies
- **FluentAssertions** for readable assertions
- **EntityFrameworkCore.InMemory** for database testing

### Test Categories

1. **PluginInitializationTests**
   - Tests plugin metadata (Identifier, Name)
   - Tests plugin initialization without crashing
   - Tests service registration

2. **RateServiceTests**
   - Tests currency conversion
   - Tests fallback behavior when providers unavailable
   - Tests null handling

3. **BitcoinRewardsSettingsTests**
   - Tests settings validation
   - Tests credential checking methods
   - Tests default values

4. **PluginIntegrationTests**
   - Tests plugin initialization in isolation
   - Tests service provider building
   - Tests with test harness

### Test Harness

Created `PluginTestHarness` class that:

- Sets up minimal BTCPay Server dependencies
- Mocks required services
- Allows testing plugin without full BTCPay Server instance
- Uses in-memory database for testing

## How to Test

### Run Unit Tests

```bash
cd BTCPayServer.Plugins.BitcoinRewards.Tests
dotnet test
```

### Test Plugin Initialization

```csharp
var harness = new PluginTestHarness();
harness.RegisterPlugin();
var provider = harness.BuildServiceProvider();
// Verify services can be resolved
```

### Test with Missing Dependencies

The plugin now throws clear errors when required services are missing:

```csharp
// Should throw InvalidOperationException with clear message
Assert.Throws<InvalidOperationException>(() => plugin.Execute(services));
```

## Common Crash Scenarios and Fixes

### Scenario 1: RateProviderFactory Not Available

**Before**: Plugin would crash with NullReferenceException
**After**: Plugin throws `InvalidOperationException` with message: "RateProviderFactory service is required but not available"

**Fix Location**: `BitcoinRewardsPlugin.cs` line 48-58

### Scenario 2: Logs Service Not Available

**Before**: Plugin would crash with InvalidOperationException from GetRequiredService
**After**: Plugin tries GetService first, then GetRequiredService, with clear error message

**Fix Location**: `BitcoinRewardsPlugin.cs` line 30-45

### Scenario 3: EventAggregator Not Available

**Before**: BitcoinRewardsService would fail to initialize
**After**: Plugin validates EventAggregator is available before registering service

**Fix Location**: `BitcoinRewardsPlugin.cs` line 60-75

### Scenario 4: RateProviderFactory.Providers is Null

**Before**: RateService would crash with NullReferenceException
**After**: RateService checks for null before accessing Providers

**Fix Location**: `RateService.cs` line 38, 43

## Debugging Tips

1. **Check BTCPay Server Logs**
   - Look for "Bitcoin Rewards Plugin" in logs
   - Check for InvalidOperationException messages
   - Verify which service is missing

2. **Test Plugin Initialization**
   - Use the test harness to test in isolation
   - Verify all required services are available
   - Check service registration order

3. **Verify Service Availability**
   - Ensure BTCPay Server is fully started
   - Check that all BTCPay Server services are registered
   - Verify plugin is loading after BTCPay Server initialization

## Next Steps

1. **Run Tests**: Execute unit tests to verify fixes
2. **Test in BTCPay Server**: Load plugin in actual BTCPay Server instance
3. **Monitor Logs**: Watch for any remaining errors
4. **Add More Tests**: Expand test coverage for edge cases

## Files Modified

- `BTCPayServer.Plugins.BitcoinRewards/BitcoinRewardsPlugin.cs` - Added defensive initialization
- `BTCPayServer.Plugins.BitcoinRewards/Services/RateService.cs` - Added null checks
- `BTCPayServer.Plugins.BitcoinRewards.Tests/` - Created test project

## Files Created

- `BTCPayServer.Plugins.BitcoinRewards.Tests/BTCPayServer.Plugins.BitcoinRewards.Tests.csproj`
- `BTCPayServer.Plugins.BitcoinRewards.Tests/PluginInitializationTests.cs`
- `BTCPayServer.Plugins.BitcoinRewards.Tests/Services/RateServiceTests.cs`
- `BTCPayServer.Plugins.BitcoinRewards.Tests/Models/BitcoinRewardsSettingsTests.cs`
- `BTCPayServer.Plugins.BitcoinRewards.Tests/TestHarness/PluginTestHarness.cs`
- `BTCPayServer.Plugins.BitcoinRewards.Tests/Integration/PluginIntegrationTests.cs`
- `TESTING_GUIDE.md` - Comprehensive testing guide
- `CRASH_FIXES.md` - This document


