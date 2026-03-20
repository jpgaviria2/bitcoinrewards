using System;
using System.Collections.Generic;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests;

/// <summary>
/// Test helpers for creating mock objects and in-memory databases
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Create an in-memory database context for testing
    /// </summary>
    public static BitcoinRewardsPluginDbContext CreateInMemoryContext(string dbName = "TestDb")
    {
        var options = new DbContextOptionsBuilder<BitcoinRewardsPluginDbContext>()
            .UseInMemoryDatabase(databaseName: dbName + Guid.NewGuid().ToString())
            .Options;

        return new BitcoinRewardsPluginDbContext(options);
    }

    /// <summary>
    /// Create a mock logger
    /// </summary>
    public static ILogger<T> CreateMockLogger<T>()
    {
        return new Mock<ILogger<T>>().Object;
    }

    /// <summary>
    /// Create a test wallet with default values
    /// </summary>
    public static CustomerWallet CreateTestWallet(
        Guid? id = null,
        string storeId = "test-store",
        string pullPaymentId = "test-pp",
        long cadBalanceCents = 1000,
        long satsBalance = 10000)
    {
        return new CustomerWallet
        {
            Id = id ?? Guid.NewGuid(),
            StoreId = storeId,
            PullPaymentId = pullPaymentId,
            CadBalanceCents = cadBalanceCents,
            SatsBalanceSatoshis = satsBalance,
            AutoConvertToCad = true,
            CreatedAt = DateTime.UtcNow,
            WalletToken = GenerateWalletToken()
        };
    }

    /// <summary>
    /// Generate a random wallet token
    /// </summary>
    public static string GenerateWalletToken()
    {
        return Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    }

    /// <summary>
    /// Create a test BOLT11 invoice string (mock - not a real invoice)
    /// </summary>
    public static string CreateTestBolt11Invoice(long sats = 1000)
    {
        // This is a mock invoice for testing - not a real BOLT11 invoice
        // Real tests should use BTCPayServer's BOLT11PaymentRequest.TryParse
        return $"lnbc{sats}u1test";
    }

    /// <summary>
    /// Seed database with test data
    /// </summary>
    public static async Task SeedTestDataAsync(BitcoinRewardsPluginDbContext ctx, params CustomerWallet[] wallets)
    {
        await ctx.CustomerWallets.AddRangeAsync(wallets);
        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Assert that two decimal values are approximately equal (for exchange rates)
    /// </summary>
    public static void AssertApproximatelyEqual(decimal expected, decimal actual, decimal tolerance = 0.01m)
    {
        var diff = Math.Abs(expected - actual);
        if (diff > tolerance)
        {
            throw new Xunit.Sdk.XunitException(
                $"Expected {expected} ± {tolerance}, but got {actual} (diff: {diff})");
        }
    }
}
