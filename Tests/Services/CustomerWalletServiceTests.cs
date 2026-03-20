using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests.Services;

/// <summary>
/// Tests for CustomerWalletService - core wallet balance operations
/// </summary>
public class CustomerWalletServiceTests : IDisposable
{
    private readonly BitcoinRewardsPluginDbContext _context;
    private readonly Mock<BitcoinRewardsPluginDbContextFactory> _mockFactory;
    private readonly Mock<BoltCardRewardService> _mockBoltCardService;
    private readonly Mock<ExchangeRateService> _mockExchangeService;
    private readonly CustomerWalletService _service;

    public CustomerWalletServiceTests()
    {
        _context = TestHelpers.CreateInMemoryContext();
        
        _mockFactory = new Mock<BitcoinRewardsPluginDbContextFactory>();
        _mockFactory.Setup(f => f.CreateContext()).Returns(_context);

        _mockBoltCardService = new Mock<BoltCardRewardService>();
        _mockExchangeService = new Mock<ExchangeRateService>();

        _service = new CustomerWalletService(
            _mockFactory.Object,
            _mockBoltCardService.Object,
            _mockExchangeService.Object,
            TestHelpers.CreateMockLogger<CustomerWalletService>()
        );
    }

    [Fact]
    public async Task SpendCadAsync_SufficientBalance_ShouldSucceed()
    {
        // Arrange
        var wallet = TestHelpers.CreateTestWallet(cadBalanceCents: 1000);
        await _context.CustomerWallets.AddAsync(wallet);
        await _context.SaveChangesAsync();

        // Act
        var (success, error) = await _service.SpendCadAsync(wallet.Id, 500, "test-payment");

        // Assert
        Assert.True(success);
        Assert.Null(error);
        
        var updated = await _context.CustomerWallets.FindAsync(wallet.Id);
        Assert.Equal(500, updated!.CadBalanceCents);
    }

    [Fact]
    public async Task SpendCadAsync_InsufficientBalance_ShouldFail()
    {
        // Arrange
        var wallet = TestHelpers.CreateTestWallet(cadBalanceCents: 100);
        await _context.CustomerWallets.AddAsync(wallet);
        await _context.SaveChangesAsync();

        // Act
        var (success, error) = await _service.SpendCadAsync(wallet.Id, 500, "test-payment");

        // Assert
        Assert.False(success);
        Assert.NotNull(error);
        Assert.Contains("Insufficient", error);
        
        // Balance should not change
        var updated = await _context.CustomerWallets.FindAsync(wallet.Id);
        Assert.Equal(100, updated!.CadBalanceCents);
    }

    [Fact]
    public async Task SpendCadAsync_WalletNotFound_ShouldFail()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var (success, error) = await _service.SpendCadAsync(nonExistentId, 100, "test");

        // Assert
        Assert.False(success);
        Assert.Contains("not found", error);
    }

    [Fact]
    public async Task SpendCadAsync_ShouldCreateTransaction()
    {
        // Arrange
        var wallet = TestHelpers.CreateTestWallet(cadBalanceCents: 1000);
        await _context.CustomerWallets.AddAsync(wallet);
        await _context.SaveChangesAsync();

        // Act
        await _service.SpendCadAsync(wallet.Id, 300, "payment-ref");

        // Assert
        var transaction = await _context.WalletTransactions
            .FirstOrDefaultAsync(t => t.CustomerWalletId == wallet.Id);
        
        Assert.NotNull(transaction);
        Assert.Equal(WalletTransactionType.CadSpent, transaction.Type);
        Assert.Equal(-300, transaction.CadCentsAmount);
        Assert.Equal("payment-ref", transaction.Reference);
    }

    [Fact]
    public async Task CreditCadAsync_ShouldIncreaseBalance()
    {
        // Arrange
        var wallet = TestHelpers.CreateTestWallet(cadBalanceCents: 100);
        await _context.CustomerWallets.AddAsync(wallet);
        await _context.SaveChangesAsync();

        // Act
        var success = await _service.CreditCadAsync(wallet.Id, 500, 10000, 0.05m, "reward");

        // Assert
        Assert.True(success);
        
        var updated = await _context.CustomerWallets.FindAsync(wallet.Id);
        Assert.Equal(600, updated!.CadBalanceCents);
    }

    [Fact]
    public async Task CreditSatsAsync_ShouldIncreaseBalance()
    {
        // Arrange
        var wallet = TestHelpers.CreateTestWallet(satsBalance: 1000);
        await _context.CustomerWallets.AddAsync(wallet);
        await _context.SaveChangesAsync();

        // Act
        var success = await _service.CreditSatsAsync(wallet.Id, 500, "top-up");

        // Assert
        Assert.True(success);
        
        var updated = await _context.CustomerWallets.FindAsync(wallet.Id);
        Assert.Equal(1500, updated!.SatsBalanceSatoshis);
    }

    [Fact]
    public async Task GetBalanceAsync_ExistingWallet_ShouldReturnBalance()
    {
        // Arrange
        var wallet = TestHelpers.CreateTestWallet(
            cadBalanceCents: 1234,
            satsBalance: 5678
        );
        await _context.CustomerWallets.AddAsync(wallet);
        await _context.SaveChangesAsync();

        // Act
        var balance = await _service.GetBalanceAsync(wallet.Id);

        // Assert
        Assert.NotNull(balance);
        Assert.Equal(1234, balance.CadBalanceCents);
        Assert.Equal(5678, balance.SatsBalance);
    }

    [Fact]
    public async Task GetBalanceAsync_NonExistent_ShouldReturnNull()
    {
        // Act
        var balance = await _service.GetBalanceAsync(Guid.NewGuid());

        // Assert
        Assert.Null(balance);
    }

    [Fact]
    public async Task ConcurrentSpends_ShouldMaintainConsistency()
    {
        // Arrange
        var wallet = TestHelpers.CreateTestWallet(cadBalanceCents: 10000);
        await _context.CustomerWallets.AddAsync(wallet);
        await _context.SaveChangesAsync();

        // Act - 10 concurrent spends of 500 each
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _service.SpendCadAsync(wallet.Id, 500, "concurrent-test"))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - should have 10 successful spends
        var successCount = tasks.Count(t => t.Result.Success);
        Assert.Equal(10, successCount);

        var updated = await _context.CustomerWallets.FindAsync(wallet.Id);
        Assert.Equal(5000, updated!.CadBalanceCents); // 10000 - (10 * 500)
    }

    [Fact]
    public async Task SpendCad_ZeroAmount_ShouldFail()
    {
        // Arrange
        var wallet = TestHelpers.CreateTestWallet();
        await _context.CustomerWallets.AddAsync(wallet);
        await _context.SaveChangesAsync();

        // Act
        var (success, error) = await _service.SpendCadAsync(wallet.Id, 0, "test");

        // Assert
        Assert.False(success);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task SpendCad_NegativeAmount_ShouldFail()
    {
        // Arrange
        var wallet = TestHelpers.CreateTestWallet();
        await _context.CustomerWallets.AddAsync(wallet);
        await _context.SaveChangesAsync();

        // Act
        var (success, error) = await _service.SpendCadAsync(wallet.Id, -100, "test");

        // Assert
        Assert.False(success);
        Assert.NotNull(error);
    }

    public void Dispose()
    {
        _context?.Dispose();
    }
}
