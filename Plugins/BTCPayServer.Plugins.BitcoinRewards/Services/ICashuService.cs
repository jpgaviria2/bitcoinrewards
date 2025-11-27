#nullable enable
using System.Threading.Tasks;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

public interface ICashuService
{
    /// <summary>
    /// Mint an ecash token for the specified amount in satoshis
    /// </summary>
    Task<string?> MintTokenAsync(long amountSatoshis, string storeId);

    /// <summary>
    /// Reclaim an unclaimed/expired ecash token back to the wallet
    /// </summary>
    Task<bool> ReclaimTokenAsync(string ecashToken, string storeId);

    /// <summary>
    /// Validate that an ecash token is still valid and unclaimed
    /// </summary>
    Task<bool> ValidateTokenAsync(string ecashToken);

    /// <summary>
    /// Get Lightning wallet balance for a store
    /// </summary>
    Task<long> GetLightningBalanceAsync(string storeId);

    /// <summary>
    /// Receive a Cashu token (from QR code or paste) and store the proofs
    /// </summary>
    Task<(bool Success, string? ErrorMessage, ulong? Amount)> ReceiveTokenAsync(string token, string storeId);
}

