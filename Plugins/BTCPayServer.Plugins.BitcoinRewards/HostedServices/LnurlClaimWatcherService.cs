#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.BitcoinRewards.HostedServices;

/// <summary>
/// Background service that polls pending LNURL-withdraw claims every 5 seconds.
/// When a Lightning invoice is paid, credits the customer wallet and marks the claim complete.
/// When an invoice expires, marks the claim as failed.
/// Uses IServiceScopeFactory to resolve scoped services per iteration.
/// </summary>
public class LnurlClaimWatcherService : BackgroundService
{
    private readonly BitcoinRewardsPluginDbContextFactory _dbFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PaymentMethodHandlerDictionary _paymentHandlers;
    private readonly LightningClientFactoryService _lightningClientFactory;
    private readonly IOptions<LightningNetworkOptions> _lightningOptions;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly ILogger<LnurlClaimWatcherService> _logger;

    public LnurlClaimWatcherService(
        BitcoinRewardsPluginDbContextFactory dbFactory,
        IServiceScopeFactory scopeFactory,
        PaymentMethodHandlerDictionary paymentHandlers,
        LightningClientFactoryService lightningClientFactory,
        IOptions<LightningNetworkOptions> lightningOptions,
        BTCPayNetworkProvider networkProvider,
        ILogger<LnurlClaimWatcherService> logger)
    {
        _dbFactory = dbFactory;
        _scopeFactory = scopeFactory;
        _paymentHandlers = paymentHandlers;
        _lightningClientFactory = lightningClientFactory;
        _lightningOptions = lightningOptions;
        _networkProvider = networkProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LnurlClaimWatcherService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingClaimsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing pending LNURL claims");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("LnurlClaimWatcherService stopped");
    }

    private async Task ProcessPendingClaimsAsync(CancellationToken ct)
    {
        await using var db = _dbFactory.CreateContext();
        var now = DateTime.UtcNow;

        var pendingClaims = await db.PendingLnurlClaims
            .Where(c => !c.IsCompleted && !c.IsFailed)
            .ToListAsync(ct);

        if (pendingClaims.Count == 0)
            return;

        _logger.LogInformation("Processing {Count} pending LNURL claims", pendingClaims.Count);

        var network = _networkProvider.GetNetwork<BTCPayNetwork>("BTC");
        if (network == null)
        {
            _logger.LogWarning("BTC network not available, skipping pending claim check");
            return;
        }

        // Create a scope to resolve scoped services (CustomerWalletService, ExchangeRateService, StoreRepository)
        using var scope = _scopeFactory.CreateScope();
        var walletService = scope.ServiceProvider.GetRequiredService<CustomerWalletService>();
        var exchangeRateService = scope.ServiceProvider.GetRequiredService<ExchangeRateService>();
        var storeRepository = scope.ServiceProvider.GetRequiredService<StoreRepository>();

        foreach (var claim in pendingClaims)
        {
            try
            {
                // Check if expired
                if (claim.ExpiresAt < now)
                {
                    claim.IsFailed = true;
                    _logger.LogInformation("Pending LNURL claim {ClaimId} expired (invoice {InvoiceId})",
                        claim.Id, claim.LightningInvoiceId);
                    continue;
                }

                var lightningClient = await GetLightningClientAsync(claim.StoreId, network, storeRepository);
                if (lightningClient == null)
                {
                    _logger.LogWarning("No Lightning client for store {StoreId}, skipping claim {ClaimId}",
                        claim.StoreId, claim.Id);
                    continue;
                }

                // Use a timeout for GetInvoice — some LN backends (e.g. Strike) may hang
                using var invoiceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                invoiceCts.CancelAfter(TimeSpan.FromSeconds(10));
                LightningInvoice? invoice;
                try
                {
                    invoice = await lightningClient.GetInvoice(claim.LightningInvoiceId, invoiceCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning("GetInvoice timed out for claim {ClaimId} (invoice {InvoiceId}), will retry",
                        claim.Id, claim.LightningInvoiceId);
                    continue;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "GetInvoice failed for claim {ClaimId} (invoice {InvoiceId}), will retry",
                        claim.Id, claim.LightningInvoiceId);
                    continue;
                }
                if (invoice == null)
                {
                    _logger.LogWarning("GetInvoice returned null for claim {ClaimId} (invoice {InvoiceId})",
                        claim.Id, claim.LightningInvoiceId);
                    continue;
                }
                if (invoice.Status != LightningInvoiceStatus.Paid)
                {
                    _logger.LogInformation("Invoice {InvoiceId} status: {Status} for claim {ClaimId}",
                        claim.LightningInvoiceId, invoice.Status, claim.Id);
                    continue;
                }

                // Invoice is paid — credit the wallet
                var receivedSats = invoice.Amount?.ToUnit(LightMoneyUnit.Satoshi) ?? claim.ExpectedSats;
                var (cadCents, exchangeRate) = await exchangeRateService.SatsToCadCentsAsync(
                    (long)receivedSats, claim.StoreId);

                await walletService.CreditCadAsync(
                    claim.CustomerWalletId,
                    cadCents,
                    (long)receivedSats,
                    exchangeRate,
                    $"lnurl-withdraw:{claim.K1Prefix ?? "bg"}");

                claim.IsCompleted = true;
                _logger.LogInformation(
                    "Background credited wallet {WalletId}: {Sats} sats ({CadCents} CAD cents) from LNURL claim {ClaimId}",
                    claim.CustomerWalletId, (long)receivedSats, cadCents, claim.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking pending LNURL claim {ClaimId}", claim.Id);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<ILightningClient?> GetLightningClientAsync(string storeId, BTCPayNetwork network, StoreRepository storeRepository)
    {
        var store = await storeRepository.FindStore(storeId);
        if (store == null) return null;

        var lnConfig = _paymentHandlers.GetLightningConfig(store, network);
        if (lnConfig == null) return null;

        var connStr = lnConfig.GetExternalLightningUrl();
        if (!string.IsNullOrEmpty(connStr))
            return _lightningClientFactory.Create(connStr, network);

        if (lnConfig.IsInternalNode &&
            _lightningOptions.Value.InternalLightningByCryptoCode.TryGetValue("BTC", out var internalClient))
            return internalClient;

        return null;
    }
}
