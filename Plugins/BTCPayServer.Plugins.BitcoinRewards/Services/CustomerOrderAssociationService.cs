#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

public sealed class CustomerOrderAssociationService
{
    private readonly BitcoinRewardsPluginDbContextFactory _dbContextFactory;
    private readonly CustomerProfileClient _profileClient;

    public CustomerOrderAssociationService(
        BitcoinRewardsPluginDbContextFactory dbContextFactory,
        CustomerProfileClient profileClient)
    {
        _dbContextFactory = dbContextFactory;
        _profileClient = profileClient;
    }

    public async Task<CustomerOrderAssociation> AssociateAsync(
        string storeId,
        string squareOrderId,
        string lightningAddress,
        string? registerId,
        string? deviceId,
        string? staffActorId,
        CancellationToken cancellationToken = default)
    {
        storeId = Required(storeId, 50, nameof(storeId));
        squareOrderId = Required(squareOrderId, 255, nameof(squareOrderId));
        var resolved = await _profileClient.ResolveAsync(storeId, lightningAddress, cancellationToken)
            ?? throw new CustomerProfileNotFoundException();

        await using var context = _dbContextFactory.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var existing = await context.CustomerOrderAssociations.SingleOrDefaultAsync(
            item => item.StoreId == storeId && item.SquareOrderId == squareOrderId,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.State != CustomerOrderAssociationState.Pending ||
                !string.Equals(existing.CustomerProfileId, resolved.ProfileId, StringComparison.Ordinal))
                throw new CustomerOrderAssociationConflictException();
            return existing;
        }

        var association = new CustomerOrderAssociation
        {
            StoreId = storeId,
            SquareOrderId = squareOrderId,
            CustomerProfileId = resolved.ProfileId,
            LightningAddressHash = resolved.LightningAddressHash,
            LightningAddress = CustomerProfileClient.NormalizeLightningAddress(lightningAddress),
            RegisterId = Optional(registerId, 100),
            DeviceId = Optional(deviceId, 100),
            StaffActorId = Optional(staffActorId, 100)
        };
        context.CustomerOrderAssociations.Add(association);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return association;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            await using var retry = _dbContextFactory.CreateContext();
            var winner = await retry.CustomerOrderAssociations.AsNoTracking().SingleOrDefaultAsync(
                item => item.StoreId == storeId && item.SquareOrderId == squareOrderId,
                cancellationToken);
            if (winner is not null && winner.State == CustomerOrderAssociationState.Pending &&
                string.Equals(winner.CustomerProfileId, resolved.ProfileId, StringComparison.Ordinal))
                return winner;
            throw new CustomerOrderAssociationConflictException();
        }
    }

    public async Task<CustomerOrderAssociation?> BindPaymentAsync(
        string storeId,
        string squareOrderId,
        string squarePaymentId,
        CancellationToken cancellationToken = default)
    {
        await using var context = _dbContextFactory.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var association = await context.CustomerOrderAssociations.SingleOrDefaultAsync(
            item => item.StoreId == storeId && item.SquareOrderId == squareOrderId,
            cancellationToken);
        if (association is null || association.State == CustomerOrderAssociationState.Cancelled)
            return null;
        if (association.SquarePaymentId is not null &&
            !string.Equals(association.SquarePaymentId, squarePaymentId, StringComparison.Ordinal))
            throw new CustomerOrderAssociationConflictException();
        if (association.State == CustomerOrderAssociationState.Consumed)
            return association;

        association.SquarePaymentId = squarePaymentId;
        association.State = CustomerOrderAssociationState.BoundToPayment;
        association.BoundAt ??= DateTime.UtcNow;
        association.UpdatedAt = DateTime.UtcNow;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return association;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            await using var retry = _dbContextFactory.CreateContext();
            var winner = await retry.CustomerOrderAssociations.AsNoTracking().SingleOrDefaultAsync(
                item => item.StoreId == storeId && item.SquareOrderId == squareOrderId,
                cancellationToken);
            if (winner is not null && string.Equals(winner.SquarePaymentId, squarePaymentId, StringComparison.Ordinal))
                return winner;
            throw new CustomerOrderAssociationConflictException();
        }
    }

    public async Task<bool> CancelAsync(string storeId, string squareOrderId, CancellationToken cancellationToken = default)
    {
        await using var context = _dbContextFactory.CreateContext();
        var association = await context.CustomerOrderAssociations.SingleOrDefaultAsync(
            item => item.StoreId == storeId && item.SquareOrderId == squareOrderId,
            cancellationToken);
        if (association is null) return false;
        if (association.State != CustomerOrderAssociationState.Pending)
            throw new CustomerOrderAssociationConflictException();
        association.State = CustomerOrderAssociationState.Cancelled;
        association.CancelledAt = DateTime.UtcNow;
        association.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task MarkConsumedAsync(Guid associationId, CancellationToken cancellationToken = default)
    {
        await using var context = _dbContextFactory.CreateContext();
        var association = await context.CustomerOrderAssociations.SingleOrDefaultAsync(
            item => item.Id == associationId, cancellationToken);
        if (association is null) throw new CustomerOrderAssociationConflictException();
        if (association.State == CustomerOrderAssociationState.Consumed) return;
        if (association.State != CustomerOrderAssociationState.BoundToPayment)
            throw new CustomerOrderAssociationConflictException();
        association.State = CustomerOrderAssociationState.Consumed;
        association.ConsumedAt = DateTime.UtcNow;
        association.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static string Required(string? value, int max, string name) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
            ? value.Trim()
            : throw new ArgumentException($"{name} is required.", name);
    private static string? Optional(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}

public sealed class CustomerProfileNotFoundException : Exception { }
public sealed class CustomerOrderAssociationConflictException : Exception { }
