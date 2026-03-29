#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Exceptions;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

public class ErrorTrackingService
{
    private readonly BitcoinRewardsDbContextFactory _dbFactory;
    private readonly ILogger<ErrorTrackingService> _logger;
    
    public ErrorTrackingService(
        BitcoinRewardsDbContextFactory dbFactory,
        ILogger<ErrorTrackingService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }
    
    /// <summary>
    /// Log a Bitcoin Rewards exception to the database and logs
    /// </summary>
    public async Task<RewardError> LogErrorAsync(
        BitcoinRewardsException exception,
        string? userId = null)
    {
        await using var db = _dbFactory.CreateContext();
        
        var error = new RewardError
        {
            Id = Guid.NewGuid().ToString(),
            ErrorType = exception.ErrorType.ToString(),
            Message = exception.Message,
            StackTrace = exception.StackTrace ?? exception.InnerException?.StackTrace,
            OrderId = exception.OrderId,
            StoreId = exception.StoreId,
            RewardId = exception.RewardId,
            Context = JsonSerializer.Serialize(exception.Context),
            UserId = userId,
            Timestamp = DateTime.UtcNow,
            Resolved = false
        };
        
        db.RewardErrors.Add(error);
        await db.SaveChangesAsync();
        
        // Structured logging with all context
        _logger.LogError(
            exception,
            "Reward error {ErrorType} for order {OrderId} in store {StoreId}: {Message}. Error ID: {ErrorId}",
            exception.ErrorType,
            exception.OrderId ?? "N/A",
            exception.StoreId ?? "N/A",
            exception.Message,
            error.Id);
        
        return error;
    }
    
    /// <summary>
    /// Log a general exception (not a BitcoinRewardsException)
    /// </summary>
    public async Task<RewardError> LogExceptionAsync(
        Exception exception,
        RewardErrorType errorType,
        string? orderId = null,
        string? storeId = null,
        string? userId = null,
        Dictionary<string, object>? context = null)
    {
        await using var db = _dbFactory.CreateContext();
        
        var error = new RewardError
        {
            Id = Guid.NewGuid().ToString(),
            ErrorType = errorType.ToString(),
            Message = exception.Message,
            StackTrace = exception.StackTrace,
            OrderId = orderId,
            StoreId = storeId,
            Context = context != null ? JsonSerializer.Serialize(context) : null,
            UserId = userId,
            Timestamp = DateTime.UtcNow,
            Resolved = false
        };
        
        db.RewardErrors.Add(error);
        await db.SaveChangesAsync();
        
        _logger.LogError(
            exception,
            "Error {ErrorType} logged: {Message}. Error ID: {ErrorId}",
            errorType,
            exception.Message,
            error.Id);
        
        return error;
    }
    
    /// <summary>
    /// Get recent errors with optional filtering
    /// </summary>
    public async Task<List<RewardError>> GetRecentErrorsAsync(
        int limit = 50,
        RewardErrorType? filterType = null,
        string? storeId = null,
        bool? resolvedOnly = null)
    {
        await using var db = _dbFactory.CreateContext();
        
        var query = db.RewardErrors.AsQueryable();
        
        if (filterType.HasValue)
        {
            query = query.Where(e => e.ErrorType == filterType.ToString());
        }
        
        if (!string.IsNullOrEmpty(storeId))
        {
            query = query.Where(e => e.StoreId == storeId);
        }
        
        if (resolvedOnly.HasValue)
        {
            query = query.Where(e => e.Resolved == resolvedOnly.Value);
        }
        
        return await query
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .ToListAsync();
    }
    
    /// <summary>
    /// Get error statistics for a store or globally
    /// </summary>
    public async Task<ErrorStatistics> GetErrorStatisticsAsync(
        string? storeId = null,
        DateTime? since = null)
    {
        await using var db = _dbFactory.CreateContext();
        
        var query = db.RewardErrors.AsQueryable();
        
        if (!string.IsNullOrEmpty(storeId))
        {
            query = query.Where(e => e.StoreId == storeId);
        }
        
        if (since.HasValue)
        {
            query = query.Where(e => e.Timestamp >= since.Value);
        }
        
        var errors = await query.ToListAsync();
        
        return new ErrorStatistics
        {
            TotalErrors = errors.Count,
            ResolvedErrors = errors.Count(e => e.Resolved),
            UnresolvedErrors = errors.Count(e => !e.Resolved),
            ErrorsByType = errors
                .GroupBy(e => e.ErrorType)
                .ToDictionary(g => g.Key, g => g.Count()),
            MostCommonError = errors
                .GroupBy(e => e.ErrorType)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault(),
            AverageResolutionTimeHours = errors
                .Where(e => e.Resolved && e.ResolvedAt.HasValue)
                .Select(e => (e.ResolvedAt!.Value - e.Timestamp).TotalHours)
                .DefaultIfEmpty(0)
                .Average()
        };
    }
    
    /// <summary>
    /// Mark an error as resolved
    /// </summary>
    public async Task<bool> ResolveErrorAsync(
        string errorId,
        string? resolvedBy = null,
        string? notes = null)
    {
        await using var db = _dbFactory.CreateContext();
        
        var error = await db.RewardErrors.FindAsync(errorId);
        if (error == null)
            return false;
        
        error.Resolved = true;
        error.ResolvedAt = DateTime.UtcNow;
        error.ResolvedBy = resolvedBy;
        error.ResolutionNotes = notes;
        
        await db.SaveChangesAsync();
        
        _logger.LogInformation(
            "Error {ErrorId} ({ErrorType}) resolved by {ResolvedBy}",
            errorId,
            error.ErrorType,
            resolvedBy ?? "system");
        
        return true;
    }
    
    /// <summary>
    /// Retry a failed reward operation
    /// </summary>
    public async Task<bool> RecordRetryAttemptAsync(string errorId)
    {
        await using var db = _dbFactory.CreateContext();
        
        var error = await db.RewardErrors.FindAsync(errorId);
        if (error == null)
            return false;
        
        error.RetryCount++;
        error.LastRetryAt = DateTime.UtcNow;
        
        await db.SaveChangesAsync();
        
        _logger.LogInformation(
            "Retry attempt #{RetryCount} recorded for error {ErrorId} ({ErrorType})",
            error.RetryCount,
            errorId,
            error.ErrorType);
        
        return true;
    }
    
    /// <summary>
    /// Get errors that might be retryable
    /// </summary>
    public async Task<List<RewardError>> GetRetryableErrorsAsync(
        int maxRetries = 3,
        int limit = 20)
    {
        await using var db = _dbFactory.CreateContext();
        
        // Get unresolved errors that haven't exceeded max retries
        // and are of types we know are retryable
        var retryableTypes = new[]
        {
            RewardErrorType.LightningNodeUnreachable.ToString(),
            RewardErrorType.DatabaseConnectionFailed.ToString(),
            RewardErrorType.ExternalServiceTimeout.ToString(),
            RewardErrorType.SquareApiError.ToString(),
            RewardErrorType.ShopifyApiError.ToString()
        };
        
        return await db.RewardErrors
            .Where(e => !e.Resolved)
            .Where(e => e.RetryCount < maxRetries)
            .Where(e => retryableTypes.Contains(e.ErrorType))
            .OrderBy(e => e.Timestamp)
            .Take(limit)
            .ToListAsync();
    }
}

/// <summary>
/// Error statistics data transfer object
/// </summary>
public class ErrorStatistics
{
    public int TotalErrors { get; set; }
    public int ResolvedErrors { get; set; }
    public int UnresolvedErrors { get; set; }
    public Dictionary<string, int> ErrorsByType { get; set; } = new();
    public string? MostCommonError { get; set; }
    public double AverageResolutionTimeHours { get; set; }
}
