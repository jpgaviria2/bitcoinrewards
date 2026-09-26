#nullable enable
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.HostedServices;

/// <summary>
/// Converts authoritative completed pull-payment claims into durable, replay-safe customer events
/// and delivers them to the Trails API. It never changes how rewards are paid.
/// </summary>
public sealed class RewardNotificationOutboxService : BackgroundService
{
    private const int BatchSize = 50;
    private const int MaxAttempts = 12;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    private readonly BitcoinRewardsPluginDbContextFactory _dbContextFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RewardNotificationOutboxService> _logger;

    public RewardNotificationOutboxService(
        BitcoinRewardsPluginDbContextFactory dbContextFactory,
        IServiceScopeFactory scopeFactory,
        ILogger<RewardNotificationOutboxService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DiscoverSettledRewardsAsync(stoppingToken);
                await DeliverPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Customer reward notification outbox cycle failed.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    internal async Task DiscoverSettledRewardsAsync(CancellationToken cancellationToken)
    {
        await using var db = _dbContextFactory.CreateContext();
        var candidates = await db.BitcoinRewardRecords.AsNoTracking()
            .Where(r => r.Status == RewardStatus.Sent &&
                        r.CustomerProfileId != null &&
                        r.PullPaymentId != null)
            .Where(r => !db.RewardNotificationOutbox.Any(o => o.RewardId == r.Id))
            .OrderBy(r => r.CreatedAt)
            .Take(BatchSize)
            .Select(r => new
            {
                r.Id,
                r.PullPaymentId,
                r.CustomerProfileId,
                r.StoreId,
                r.RewardAmountSatoshis
            })
            .ToListAsync(cancellationToken);

        using var scope = _scopeFactory.CreateScope();
        var pullPayments = scope.ServiceProvider.GetRequiredService<PullPaymentStatusService>();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await pullPayments.IsPullPaymentClaimedAsync(candidate.PullPaymentId!)) continue;

            await using var updateDb = _dbContextFactory.CreateContext();
            await using var transaction = await updateDb.Database.BeginTransactionAsync(cancellationToken);
            var occurredAt = DateTime.UtcNow;
            var updated = await updateDb.BitcoinRewardRecords
                .Where(r => r.Id == candidate.Id && r.Status == RewardStatus.Sent)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.Status, RewardStatus.Redeemed)
                    .SetProperty(r => r.ClaimedAt, r => r.ClaimedAt ?? occurredAt)
                    .SetProperty(r => r.RedeemedAt, r => r.RedeemedAt ?? occurredAt)
                    .SetProperty(r => r.PaidAt, r => r.PaidAt ?? occurredAt), cancellationToken);
            if (updated != 1 || string.IsNullOrWhiteSpace(candidate.CustomerProfileId))
            {
                await transaction.RollbackAsync(cancellationToken);
                continue;
            }

            updateDb.RewardNotificationOutbox.Add(new RewardNotificationOutbox
            {
                RewardId = candidate.Id,
                EventId = $"reward:{candidate.Id:D}:settled",
                CustomerProfileId = candidate.CustomerProfileId,
                StoreId = candidate.StoreId,
                AmountSatoshis = candidate.RewardAmountSatoshis,
                OccurredAt = occurredAt,
                NextAttemptAt = occurredAt,
                CreatedAt = occurredAt,
                UpdatedAt = occurredAt
            });
            await updateDb.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Queued settled customer reward event {EventId}.",
                $"reward:{candidate.Id:D}:settled");
        }
    }

    internal async Task DeliverPendingAsync(CancellationToken cancellationToken)
    {
        await using var listDb = _dbContextFactory.CreateContext();
        var now = DateTime.UtcNow;
        var ids = await listDb.RewardNotificationOutbox.AsNoTracking()
            .Where(o =>
                (o.State == RewardNotificationState.Pending && o.NextAttemptAt <= now) ||
                (o.State == RewardNotificationState.Delivering && o.LeaseExpiresAt < now))
            .OrderBy(o => o.NextAttemptAt)
            .Take(BatchSize)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DeliverOneAsync(id, cancellationToken);
        }
    }

    private async Task DeliverOneAsync(Guid id, CancellationToken cancellationToken)
    {
        var claimedAt = DateTime.UtcNow;
        await using var claimDb = _dbContextFactory.CreateContext();
        var claimed = await claimDb.RewardNotificationOutbox
            .Where(o => o.Id == id &&
                ((o.State == RewardNotificationState.Pending && o.NextAttemptAt <= claimedAt) ||
                 (o.State == RewardNotificationState.Delivering && o.LeaseExpiresAt < claimedAt)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(o => o.State, RewardNotificationState.Delivering)
                .SetProperty(o => o.LeaseExpiresAt, claimedAt + LeaseDuration)
                .SetProperty(o => o.LastAttemptAt, claimedAt)
                .SetProperty(o => o.AttemptCount, o => o.AttemptCount + 1)
                .SetProperty(o => o.UpdatedAt, claimedAt), cancellationToken);
        if (claimed != 1) return;

        await using var readDb = _dbContextFactory.CreateContext();
        var item = await readDb.RewardNotificationOutbox.AsNoTracking()
            .SingleAsync(o => o.Id == id, cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<CustomerProfileClient>();
            await client.PublishRewardSettledAsync(
                item.StoreId,
                item.EventId,
                item.RewardId,
                item.CustomerProfileId,
                item.AmountSatoshis,
                item.OccurredAt,
                cancellationToken);
            await CompleteAsync(id, RewardNotificationState.Delivered, null, cancellationToken);
        }
        catch (CustomerProfileClient.DeliveryException ex) when (ex.Suppressed)
        {
            await CompleteAsync(id, RewardNotificationState.Suppressed, ex.Message, cancellationToken);
        }
        catch (CustomerProfileClient.DeliveryException ex) when (!ex.Retryable)
        {
            await CompleteAsync(id, RewardNotificationState.Failed, ex.Message, cancellationToken);
        }
        catch (Exception ex) when (ex is CustomerProfileClient.DeliveryException or HttpRequestException or TaskCanceledException)
        {
            await RetryAsync(id, item.AttemptCount, ex.Message, cancellationToken);
        }
    }

    private async Task CompleteAsync(
        Guid id,
        RewardNotificationState state,
        string? error,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await using var db = _dbContextFactory.CreateContext();
        await db.RewardNotificationOutbox.Where(o => o.Id == id).ExecuteUpdateAsync(setters => setters
            .SetProperty(o => o.State, state)
            .SetProperty(o => o.DeliveredAt, state == RewardNotificationState.Delivered ? now : null)
            .SetProperty(o => o.LeaseExpiresAt, (DateTime?)null)
            .SetProperty(o => o.LastError, TrimError(error))
            .SetProperty(o => o.UpdatedAt, now), cancellationToken);
    }

    private async Task RetryAsync(Guid id, int attempt, string error, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var terminal = attempt >= MaxAttempts;
        var delayMinutes = Math.Min(360, Math.Pow(2, Math.Min(attempt, 8)));
        await using var db = _dbContextFactory.CreateContext();
        await db.RewardNotificationOutbox.Where(o => o.Id == id).ExecuteUpdateAsync(setters => setters
            .SetProperty(o => o.State, terminal ? RewardNotificationState.Failed : RewardNotificationState.Pending)
            .SetProperty(o => o.NextAttemptAt, terminal ? now : now.AddMinutes(delayMinutes))
            .SetProperty(o => o.LeaseExpiresAt, (DateTime?)null)
            .SetProperty(o => o.LastError, TrimError(error))
            .SetProperty(o => o.UpdatedAt, now), cancellationToken);
    }

    private static string? TrimError(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value[..Math.Min(value.Length, 500)];
}
