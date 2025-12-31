#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Hubs;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Service to broadcast rewards to display devices via SignalR
/// </summary>
public class RewardDisplayService
{
    private readonly IHubContext<RewardDisplayHub> _hubContext;
    private readonly ILogger<RewardDisplayService> _logger;

    public RewardDisplayService(
        IHubContext<RewardDisplayHub> hubContext,
        ILogger<RewardDisplayService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Broadcast a reward to all display devices connected to a specific store
    /// </summary>
    public async Task BroadcastRewardToDisplay(string storeId, RewardDisplayMessage reward)
    {
        try
        {
            var groupName = RewardDisplayHub.GetStoreGroupName(storeId);
            
            // Send the reward message to all clients in the store's group
            await _hubContext.Clients.Group(groupName)
                .SendAsync("ReceiveReward", reward);
            
            _logger.LogInformation(
                "Broadcasted reward {TransactionId} ({Satoshis} sats) to display devices for store {StoreId}",
                reward.TransactionId, reward.RewardSatoshis, storeId);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to broadcast reward {TransactionId} to display devices for store {StoreId}",
                reward.TransactionId, storeId);
        }
    }
}

