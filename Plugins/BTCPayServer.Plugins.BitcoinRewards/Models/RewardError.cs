#nullable enable

using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.BitcoinRewards.Models;

/// <summary>
/// Database entity for tracking Bitcoin Rewards errors
/// </summary>
public class RewardError
{
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = null!;
    
    /// <summary>
    /// Error type (from RewardErrorType enum)
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string ErrorType { get; set; } = null!;
    
    /// <summary>
    /// Error message
    /// </summary>
    [Required]
    [MaxLength(2000)]
    public string Message { get; set; } = null!;
    
    /// <summary>
    /// Full stack trace (nullable for non-exception errors)
    /// </summary>
    public string? StackTrace { get; set; }
    
    /// <summary>
    /// Associated order ID (if applicable)
    /// </summary>
    [MaxLength(255)]
    public string? OrderId { get; set; }
    
    /// <summary>
    /// Store ID where error occurred
    /// </summary>
    [MaxLength(255)]
    public string? StoreId { get; set; }
    
    /// <summary>
    /// Reward ID (if applicable)
    /// </summary>
    [MaxLength(36)]
    public string? RewardId { get; set; }
    
    /// <summary>
    /// Additional context as JSON
    /// </summary>
    public string? Context { get; set; }
    
    /// <summary>
    /// User ID who encountered the error (if applicable)
    /// </summary>
    [MaxLength(255)]
    public string? UserId { get; set; }
    
    /// <summary>
    /// When the error occurred
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Whether this error has been resolved/acknowledged
    /// </summary>
    public bool Resolved { get; set; } = false;
    
    /// <summary>
    /// When the error was resolved
    /// </summary>
    public DateTime? ResolvedAt { get; set; }
    
    /// <summary>
    /// Who resolved the error
    /// </summary>
    [MaxLength(255)]
    public string? ResolvedBy { get; set; }
    
    /// <summary>
    /// Resolution notes
    /// </summary>
    [MaxLength(1000)]
    public string? ResolutionNotes { get; set; }
    
    /// <summary>
    /// Number of retry attempts (if retryable)
    /// </summary>
    public int RetryCount { get; set; } = 0;
    
    /// <summary>
    /// Last retry attempt timestamp
    /// </summary>
    public DateTime? LastRetryAt { get; set; }
}
