#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

public interface IEmailNotificationService
{
    Task<bool> SendRewardNotificationAsync(
        string recipient,
        DeliveryMethod deliveryMethod,
        decimal rewardAmountBtc,
        long rewardAmountSatoshis,
        string? pullPaymentLink,
        string? ecashToken,
        string orderId);
}

