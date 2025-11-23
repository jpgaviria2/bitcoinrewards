using System;
using System.Reflection;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services
{
    public class EmailService
    {
        private readonly object? _emailSender; // Use object to avoid compile-time dependency on optional plugin
        private readonly Logs _logs;

        public EmailService(
            Logs logs,
            object? emailSender = null) // Optional parameter - will be null if Emails plugin not installed
        {
            _emailSender = emailSender;
            _logs = logs;
        }

        public async Task<bool> SendRewardEmailAsync(
            RewardRecord rewardRecord,
            BitcoinRewardsSettings settings,
            StoreData store)
        {
            if (string.IsNullOrEmpty(rewardRecord.CustomerEmail))
            {
                _logs.PayServer.LogWarning($"Cannot send reward email: no email for reward {rewardRecord.Id}");
                return false;
            }

            // Check if email sender is available (from Emails plugin)
            if (_emailSender == null)
            {
                _logs.PayServer.LogWarning($"Email sender not available (Emails plugin not installed). Cannot send reward email for {rewardRecord.Id}");
                return false;
            }

            try
            {
                // Use reflection to call IEmailSender methods to avoid compile-time dependency
                var emailSenderType = _emailSender.GetType();
                
                // Try to find SendEmail method that takes MailboxAddress
                // First, try to get MimeKit.MailboxAddress type via reflection
                var mailboxAddressType = Type.GetType("MimeKit.MailboxAddress, MimeKit");
                if (mailboxAddressType == null)
                {
                    _logs.PayServer.LogWarning($"MimeKit.MailboxAddress type not found. Cannot send reward email for {rewardRecord.Id}");
                    return false;
                }

                var sendEmailMethod = emailSenderType.GetMethod("SendEmail", new[] { mailboxAddressType, typeof(string), typeof(string) });
                
                if (sendEmailMethod == null)
                {
                    _logs.PayServer.LogWarning($"IEmailSender.SendEmail method not found. Cannot send reward email for {rewardRecord.Id}");
                    return false;
                }

                var emailBody = settings.EmailBodyTemplate
                    .Replace("{RewardAmount}", rewardRecord.RewardAmount.ToString("F8"))
                    .Replace("{BitcoinAddress}", rewardRecord.BitcoinAddress ?? "N/A")
                    .Replace("{OrderId}", rewardRecord.OrderId)
                    .Replace("{TransactionId}", rewardRecord.TransactionId ?? "pending");

                var fromAddress = !string.IsNullOrEmpty(settings.EmailFromAddress) 
                    ? settings.EmailFromAddress 
                    : "noreply@btcpayserver.org";

                // Create MailboxAddress instances using reflection
                var parseMethod = mailboxAddressType.GetMethod("Parse", new[] { typeof(string) });
                if (parseMethod == null)
                {
                    _logs.PayServer.LogWarning($"MailboxAddress.Parse method not found. Cannot send reward email for {rewardRecord.Id}");
                    return false;
                }

                var recipientAddress = parseMethod.Invoke(null, new object[] { rewardRecord.CustomerEmail });
                var fromMailboxAddress = parseMethod.Invoke(null, new object[] { fromAddress });

                // Call SendEmail via reflection
                var result = sendEmailMethod.Invoke(_emailSender, new object[] { recipientAddress, settings.EmailSubject, emailBody });
                
                // Handle async if needed
                if (result is Task task)
                {
                    await task;
                }

                _logs.PayServer.LogInformation($"Reward email sent to {rewardRecord.CustomerEmail} for reward {rewardRecord.Id}");
                return true;
            }
            catch (Exception ex)
            {
                _logs.PayServer.LogError(ex, $"Failed to send reward email for {rewardRecord.Id}: {ex.Message}");
                return false;
            }
        }
    }
}

