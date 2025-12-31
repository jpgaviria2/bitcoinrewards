#nullable enable
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Hubs;

/// <summary>
/// SignalR Hub for broadcasting rewards to display devices in physical stores
/// </summary>
public class RewardDisplayHub : Hub
{
    private readonly ILogger<RewardDisplayHub> _logger;

    public RewardDisplayHub(ILogger<RewardDisplayHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Client calls this to join a store-specific group to receive rewards
    /// </summary>
    public async Task JoinStore(string storeId)
    {
        if (string.IsNullOrWhiteSpace(storeId))
        {
            _logger.LogWarning("Client attempted to join store with empty storeId");
            return;
        }

        var groupName = GetStoreGroupName(storeId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Client {ConnectionId} joined store group {StoreId}", Context.ConnectionId, storeId);
    }

    /// <summary>
    /// Client calls this to leave a store-specific group
    /// </summary>
    public async Task LeaveStore(string storeId)
    {
        if (string.IsNullOrWhiteSpace(storeId))
        {
            return;
        }

        var groupName = GetStoreGroupName(storeId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Client {ConnectionId} left store group {StoreId}", Context.ConnectionId, storeId);
    }

    public override async Task OnDisconnectedAsync(System.Exception? exception)
    {
        _logger.LogInformation("Client {ConnectionId} disconnected", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client {ConnectionId} connected to RewardDisplayHub", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Gets the SignalR group name for a store
    /// </summary>
    public static string GetStoreGroupName(string storeId)
    {
        return $"store_{storeId}";
    }
}

