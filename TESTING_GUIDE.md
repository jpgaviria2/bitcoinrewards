# Testing Guide for Bitcoin Rewards Plugin

This guide explains how to test the Bitcoin Rewards plugin to identify and fix issues before deploying to production.

## Table of Contents

1. [Running Unit Tests](#running-unit-tests)
2. [Using the Test Harness](#using-the-test-harness)
3. [Testing in Isolation](#testing-in-isolation)
4. [Common Issues and Fixes](#common-issues-and-fixes)
5. [Debugging Plugin Crashes](#debugging-plugin-crashes)

---

## Running Unit Tests

### Prerequisites

- .NET 8.0 SDK installed
- Test project built successfully

### Running Tests

#### Run All Tests

```bash
cd BTCPayServer.Plugins.BitcoinRewards.Tests
dotnet test
```

#### Run Specific Test Class

```bash
dotnet test --filter "FullyQualifiedName~PluginInitializationTests"
```

#### Run with Verbose Output

```bash
dotnet test --verbosity detailed
```

#### Run with Code Coverage

```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

### Test Categories

- **Unit Tests**: Test individual components in isolation
- **Integration Tests**: Test plugin initialization and service registration
- **Model Tests**: Test data models and validation

---

## Using the Test Harness

The test harness allows you to test the plugin without a full BTCPay Server instance.

### Basic Usage

```csharp
using BTCPayServer.Plugins.BitcoinRewards.Tests.TestHarness;

var harness = new PluginTestHarness();
harness.RegisterPlugin();
var provider = harness.BuildServiceProvider();

// Test that services are registered
var rateService = provider.GetService<RateService>();
Assert.NotNull(rateService);
```

### Example Test

```csharp
[Fact]
public void Plugin_Should_Initialize_Without_Crashing()
{
    var harness = new PluginTestHarness();
    
    // This should not throw
    harness.RegisterPlugin();
    
    // Service provider should build successfully
    var provider = harness.BuildServiceProvider();
    Assert.NotNull(provider);
}
```

---

## Testing in Isolation

### Testing Plugin Initialization

Create a test that verifies the plugin can initialize without crashing:

```csharp
[Fact]
public void Plugin_Execute_Should_Not_Crash()
{
    var services = new ServiceCollection();
    services.AddLogging();
    
    // Add minimal required services
    var mockLogs = new Mock<BTCPayServer.Logging.Logs>();
    services.AddSingleton(mockLogs.Object);
    
    var plugin = new BitcoinRewardsPlugin();
    
    // Should not throw
    plugin.Execute(services);
}
```

### Testing Service Registration

Verify that all required services are registered:

```csharp
[Fact]
public void Plugin_Should_Register_All_Services()
{
    var services = new ServiceCollection();
    // ... setup services ...
    
    var plugin = new BitcoinRewardsPlugin();
    plugin.Execute(services);
    
    var provider = services.BuildServiceProvider();
    
    // Verify services can be resolved
    var rateService = provider.GetService<RateService>();
    Assert.NotNull(rateService);
}
```

### Testing with Missing Dependencies

Test that the plugin handles missing dependencies gracefully:

```csharp
[Fact]
public void Plugin_Should_Handle_Missing_RateProviderFactory()
{
    var services = new ServiceCollection();
    services.AddLogging();
    
    // Don't register RateProviderFactory
    var mockLogs = new Mock<BTCPayServer.Logging.Logs>();
    services.AddSingleton(mockLogs.Object);
    
    var plugin = new BitcoinRewardsPlugin();
    
    // Should throw a clear error, not crash silently
    Assert.Throws<InvalidOperationException>(() => plugin.Execute(services));
}
```

---

## Common Issues and Fixes

### Issue: Plugin Crashes on Startup

**Symptoms**: BTCPay Server fails to start or crashes when loading the plugin.

**Possible Causes**:
1. Missing required BTCPay Server services
2. Null reference exceptions
3. Service registration conflicts

**Debugging Steps**:

1. **Check BTCPay Server Logs**
   ```bash
   # Docker
   docker logs btcpayserver
   
   # Manual
   tail -f /path/to/btcpayserver/logs/app.log
   ```

2. **Look for Plugin-Specific Errors**
   - Search for "Bitcoin Rewards" in logs
   - Look for stack traces mentioning plugin classes
   - Check for "InvalidOperationException" or "NullReferenceException"

3. **Test Plugin Initialization**
   ```csharp
   [Fact]
   public void Debug_Plugin_Initialization()
   {
       try
       {
           var services = new ServiceCollection();
           // Add all required services
           var plugin = new BitcoinRewardsPlugin();
           plugin.Execute(services);
           
           var provider = services.BuildServiceProvider();
           // Try to resolve services
       }
       catch (Exception ex)
       {
           // Log the exception to see what's missing
           Console.WriteLine($"Error: {ex.Message}");
           Console.WriteLine($"Stack: {ex.StackTrace}");
       }
   }
   ```

### Issue: RateService Fails

**Symptoms**: Plugin loads but rate conversion fails.

**Possible Causes**:
1. RateProviderFactory not available
2. No rate providers configured
3. Network issues fetching rates

**Fix**: The plugin now uses fallback rates when providers are unavailable.

### Issue: Database Context Errors

**Symptoms**: Errors about ApplicationDbContext or RewardRecordEntity.

**Possible Causes**:
1. Database migrations not run
2. Entity configuration missing
3. DbContext not registered

**Fix**: Ensure ApplicationDbContext is properly configured in BTCPay Server.

---

## Debugging Plugin Crashes

### Step 1: Enable Detailed Logging

Add logging to plugin initialization:

```csharp
public override void Execute(IServiceCollection applicationBuilder)
{
    try
    {
        // Add logging here
        Console.WriteLine("Bitcoin Rewards Plugin: Starting initialization");
        
        // ... registration code ...
        
        Console.WriteLine("Bitcoin Rewards Plugin: Initialization complete");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Bitcoin Rewards Plugin: Error - {ex.Message}");
        Console.WriteLine($"Stack: {ex.StackTrace}");
        throw;
    }
}
```

### Step 2: Test Each Service Registration

Register services one at a time to identify which one fails:

```csharp
// Test 1: Repository
applicationBuilder.AddScoped<Repositories.RewardRecordRepository>();

// Test 2: Wallet Service
applicationBuilder.AddScoped<Services.WalletService>();

// Test 3: Email Service
// ... etc
```

### Step 3: Check Service Dependencies

Verify all required services are available:

```csharp
var logs = provider.GetService<BTCPayServer.Logging.Logs>();
if (logs == null)
{
    throw new InvalidOperationException("Logs service not available");
}

var rateProviderFactory = provider.GetService<RateProviderFactory>();
if (rateProviderFactory == null)
{
    throw new InvalidOperationException("RateProviderFactory not available");
}
```

### Step 4: Test in Minimal Environment

Create a minimal test that only registers essential services:

```csharp
[Fact]
public void Minimal_Plugin_Test()
{
    var services = new ServiceCollection();
    
    // Only add absolutely required services
    services.AddLogging();
    
    // Mock only what's needed
    var mockLogs = new Mock<BTCPayServer.Logging.Logs>();
    services.AddSingleton(mockLogs.Object);
    
    var plugin = new BitcoinRewardsPlugin();
    
    try
    {
        plugin.Execute(services);
        var provider = services.BuildServiceProvider();
        
        // Try to get a service
        var walletService = provider.GetService<WalletService>();
    }
    catch (Exception ex)
    {
        // This will tell you what's missing
        Assert.True(false, $"Plugin failed: {ex.Message}");
    }
}
```

---

## Running Tests in CI/CD

### GitHub Actions Example

```yaml
name: Test Plugin

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      - name: Restore dependencies
        run: dotnet restore
      - name: Build
        run: dotnet build --no-restore
      - name: Test
        run: dotnet test --no-build --verbosity normal
```

---

## Best Practices

1. **Always Test Plugin Initialization**
   - Verify plugin can load without crashing
   - Test with missing dependencies
   - Test with all dependencies present

2. **Test Service Resolution**
   - Verify all registered services can be resolved
   - Test with mocked dependencies
   - Test error handling

3. **Test Edge Cases**
   - Null values
   - Missing configuration
   - Network failures
   - Database errors

4. **Use Test Harness for Integration Tests**
   - Don't require full BTCPay Server instance
   - Mock external dependencies
   - Test in isolation

5. **Log Everything**
   - Add logging to plugin initialization
   - Log service registration
   - Log errors with context

---

## Troubleshooting Checklist

- [ ] Plugin builds successfully
- [ ] Unit tests pass
- [ ] Integration tests pass
- [ ] Plugin initializes without errors
- [ ] All services can be resolved
- [ ] No null reference exceptions
- [ ] Missing dependencies handled gracefully
- [ ] Error messages are clear and helpful
- [ ] Logs show plugin initialization
- [ ] Plugin doesn't crash BTCPay Server

---

## Getting Help

If tests reveal issues:

1. Check BTCPay Server logs for detailed error messages
2. Review test output for specific failures
3. Verify all required BTCPay Server services are available
4. Test with minimal configuration
5. Check BTCPay Server version compatibility

For more help, see the main [README.md](README.md) or open an issue.


