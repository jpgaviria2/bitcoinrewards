#nullable enable
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Email notification service that optionally uses BTCPay Server's Email plugin if available.
/// Uses reflection to avoid compile-time dependencies on the Email plugin.
/// </summary>
public class EmailNotificationService : IEmailNotificationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EmailNotificationService> _logger;

    public EmailNotificationService(
        IServiceProvider serviceProvider,
        ILogger<EmailNotificationService> logger)
    {
        _serviceProvider = serviceProvider;
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
            // Use reflection to find and use EmailSenderFactory from Email plugin
            // This avoids compile-time dependencies that cause ReflectionTypeLoadException
            var emailsAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "BTCPayServer.Plugins.Emails" || 
                                    a.FullName?.Contains("Emails") == true);
            
            if (emailsAssembly == null)
            {
                _logger.LogDebug("Email plugin assembly not found - email notifications disabled");
                return false;
            }

            var emailFactoryType = emailsAssembly.GetType("BTCPayServer.Plugins.Emails.Services.EmailSenderFactory");
            if (emailFactoryType == null)
            {
                _logger.LogDebug("EmailSenderFactory type not found");
                return false;
            }

            var emailSenderFactory = _serviceProvider.GetService(emailFactoryType);
            if (emailSenderFactory == null)
            {
                _logger.LogDebug("EmailSenderFactory service not available");
                return false;
            }

            // Call GetEmailSender method using reflection
            var getEmailSenderMethod = emailFactoryType.GetMethod("GetEmailSender", new[] { typeof(string) });
            if (getEmailSenderMethod == null)
            {
                _logger.LogError("GetEmailSender method not found");
                return false;
            }

            var emailSenderTask = getEmailSenderMethod.Invoke(emailSenderFactory, new object?[] { null });
            if (emailSenderTask == null || !(emailSenderTask is Task emailTask))
            {
                return false;
            }

            await emailTask;
            
            // Get result from Task
            var taskResultProperty = emailTask.GetType().GetProperty("Result");
            if (taskResultProperty == null)
            {
                return false;
            }

            var emailSender = taskResultProperty.GetValue(emailTask);
            if (emailSender == null)
            {
                return false;
            }

            var subject = $"Your Bitcoin Reward - {rewardAmountSatoshis} sats";
            var body = $@"You've received a Bitcoin reward!

Order: {orderId}
Reward Amount: {rewardAmountBtc} BTC ({rewardAmountSatoshis} satoshis)

Your ecash token:
{ecashToken}

You can redeem this token using any Cashu-compatible wallet.

Thank you for your purchase!";

            // Use reflection to create MailboxAddress
            var mimeKitAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "MimeKit");
            
            if (mimeKitAssembly == null)
            {
                _logger.LogError("MimeKit assembly not found");
                return false;
            }

            var mailboxAddressType = mimeKitAssembly.GetType("MimeKit.MailboxAddress");
            if (mailboxAddressType == null)
            {
                _logger.LogError("MailboxAddress type not found");
                return false;
            }

            var parseMethod = mailboxAddressType.GetMethod("Parse", new[] { typeof(string) });
            if (parseMethod == null)
            {
                _logger.LogError("MailboxAddress.Parse method not found");
                return false;
            }

            var mailboxAddress = parseMethod.Invoke(null, new object[] { recipient });
            
            // Call SendEmail method
            var sendEmailMethod = emailSender.GetType().GetMethod("SendEmail", 
                new[] { mailboxAddressType, typeof(string), typeof(string) });
            
            if (sendEmailMethod == null)
            {
                _logger.LogError("SendEmail method not found");
                return false;
            }

            sendEmailMethod.Invoke(emailSender, new[] { mailboxAddress, subject, body });
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
