#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards;
using BTCPayServer.Plugins.Emails.Services;
using MimeKit;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

public class EmailNotificationService : IEmailNotificationService
{
    private readonly EmailSenderFactory _emailSenderFactory;
    private readonly ILogger<EmailNotificationService> _logger;

    public EmailNotificationService(
        EmailSenderFactory emailSenderFactory,
        ILogger<EmailNotificationService> logger)
    {
        _emailSenderFactory = emailSenderFactory;
        _logger = logger;
    }

    public async Task<bool> SendRewardNotificationAsync(
        string recipient,
        DeliveryMethod deliveryMethod,
        decimal rewardAmountBtc,
        long rewardAmountSatoshis,
        string ecashToken,
        string orderId)
    {
        if (deliveryMethod != DeliveryMethod.Email)
        {
            _logger.LogWarning("Email delivery requested but delivery method is {DeliveryMethod}", deliveryMethod);
            return false;
        }

        try
        {
            // Note: EmailSenderFactory needs storeId - pass null for server-level email
            // TODO: Pass storeId from reward processing service if available
            var emailSender = await _emailSenderFactory.GetEmailSender(null);
            if (emailSender == null)
            {
                _logger.LogError("Email sender not available");
                return false;
            }

            var subject = $"Your Bitcoin Reward - {rewardAmountSatoshis} sats";
            var body = $@"
You've received a Bitcoin reward!

Order: {orderId}
Reward Amount: {rewardAmountBtc} BTC ({rewardAmountSatoshis} satoshis)

Your ecash token:
{ecashToken}

You can redeem this token using any Cashu-compatible wallet.

Thank you for your purchase!
";

            var mailboxAddress = MailboxAddress.Parse(recipient);
            emailSender.SendEmail(mailboxAddress, subject, body);
            _logger.LogInformation("Reward email sent to {Recipient} for order {OrderId}", recipient, orderId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send reward email to {Recipient} for order {OrderId}", recipient, orderId);
            return false;
        }
    }
}

