#nullable enable
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards;
using Microsoft.Extensions.Logging;
using MimeKit;
using MailKit.Net.Smtp;
using BTCPayServer.Services;
using BTCPayServer.Plugins.Emails.Services;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Email notification service that optionally uses BTCPay Server's Email plugin if available.
/// Uses reflection to avoid compile-time dependencies on the Email plugin.
/// </summary>
public class EmailNotificationService : IEmailNotificationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EmailNotificationService> _logger;
    private readonly SettingsRepository _settingsRepository;

    public EmailNotificationService(
        IServiceProvider serviceProvider,
        ILogger<EmailNotificationService> logger,
        SettingsRepository settingsRepository)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _settingsRepository = settingsRepository;
    }

    public async Task<bool> SendRewardNotificationAsync(
        string recipient,
        DeliveryMethod deliveryMethod,
        decimal rewardAmountBtc,
        long rewardAmountSatoshis,
        string? pullPaymentLink,
        string storeId,
        string orderId)
    {
        if (deliveryMethod != DeliveryMethod.Email)
        {
            _logger.LogWarning("Email delivery requested but delivery method is {DeliveryMethod}", deliveryMethod);
            return false;
        }

        try
        {
            // Resolve EmailSenderFactory by type name to avoid hard dependency if emails feature is disabled
            // Prefer the Emails assembly (plugin/builtin) so we can use server email if store email is not configured.
            var emailFactoryType =
                  Type.GetType("BTCPayServer.Plugins.Emails.Services.EmailSenderFactory, BTCPayServer.Plugins.Emails")
               ?? Type.GetType("BTCPayServer.Plugins.Emails.Services.EmailSenderFactory, BTCPayServer")
               ?? TryLoadTypeFromAssembly("BTCPayServer.Plugins.Emails", "BTCPayServer.Plugins.Emails.Services.EmailSenderFactory")
               ?? TryLoadTypeFromAssembly("BTCPayServer", "BTCPayServer.Plugins.Emails.Services.EmailSenderFactory")
               ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("BTCPayServer.Plugins.Emails.Services.EmailSenderFactory"))
                    .FirstOrDefault(t => t != null)
               // Last-resort: find any EmailSenderFactory by name in loaded assemblies (namespace-agnostic)
               ?? AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(SafeGetTypes)
                    .FirstOrDefault(t => string.Equals(t.Name, "EmailSenderFactory", StringComparison.Ordinal));

            if (emailFactoryType != null)
            {
                var emailSenderFactory = _serviceProvider.GetService(emailFactoryType);
                if (emailSenderFactory != null)
                {
                    var getEmailSender = emailFactoryType.GetMethod("GetEmailSender", new[] { typeof(string) });
                    if (getEmailSender != null)
                    {
                        var emailSenderTaskObj = getEmailSender.Invoke(emailSenderFactory, new object?[] { storeId });
                        if (emailSenderTaskObj is Task emailSenderTask)
                        {
                            await emailSenderTask.ConfigureAwait(false);
                            var resultProp = emailSenderTask.GetType().GetProperty("Result");
                            var emailSender = resultProp?.GetValue(emailSenderTask);
                            if (emailSender != null)
                            {
                                return await SendViaEmailSender(emailSender, recipient, rewardAmountSatoshis, rewardAmountBtc, orderId, pullPaymentLink);
                            }
                        }
                    }
                }
            }

            // Fallback: use server SMTP settings directly if EmailSenderFactory not available
            var smtpSettings = await _settingsRepository.GetSettingAsync<EmailSettings>();
            if (smtpSettings == null || !smtpSettings.IsComplete())
            {
                _logger.LogWarning("Server email settings not available or incomplete - reward email skipped");
                return false;
            }

            var subject = $"Your Bitcoin Reward - {rewardAmountSatoshis} sats";
            var body = BuildBody(orderId, rewardAmountBtc, rewardAmountSatoshis, pullPaymentLink);
            var mailboxAddress = MailboxAddress.Parse(recipient);
            using var message = smtpSettings.CreateMailMessage(mailboxAddress, subject, body, true);
            using var client = await smtpSettings.CreateSmtpClient();
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            _logger.LogInformation("Reward email sent (server SMTP) to {Recipient} for order {OrderId}", recipient, orderId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send reward email to {Recipient} for order {OrderId}", recipient, orderId);
            return false;
        }
    }

    private async Task<bool> SendViaEmailSender(object emailSender, string recipient, long rewardAmountSatoshis, decimal rewardAmountBtc, string orderId, string? pullPaymentLink)
    {
        var subject = $"Your Bitcoin Reward - {rewardAmountSatoshis} sats";
        var body = BuildBody(orderId, rewardAmountBtc, rewardAmountSatoshis, pullPaymentLink);
        var mailboxAddress = MailboxAddress.Parse(recipient);

        var sendEmail = emailSender.GetType().GetMethod("SendEmail", new[] { typeof(MailboxAddress), typeof(string), typeof(string) });
        if (sendEmail == null)
        {
            _logger.LogWarning("SendEmail method not found on email sender - reward email skipped");
            return false;
        }

        var result = sendEmail.Invoke(emailSender, new object[] { mailboxAddress, subject, body });
        if (result is Task task)
        {
            await task.ConfigureAwait(false);
        }
        _logger.LogInformation("Reward email sent to {Recipient} for order {OrderId}", recipient, orderId);
        return true;
    }

    private string BuildBody(string orderId, decimal rewardAmountBtc, long rewardAmountSatoshis, string? pullPaymentLink)
    {
        return $@"You've received a Bitcoin reward!

Order: {orderId}
Reward Amount: {rewardAmountBtc} BTC ({rewardAmountSatoshis} satoshis)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
CLAIM YOUR REWARD:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
" + BuildClaimSection(pullPaymentLink, rewardAmountSatoshis) + @"

Thank you for your purchase!";
    }

    private static Type? TryLoadTypeFromAssembly(string assemblyName, string typeFullName)
    {
        try
        {
            var asm = Assembly.Load(assemblyName);
            return asm?.GetType(typeFullName);
        }
        catch
        {
            return null;
        }
    }

    private static Type[] SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    private string BuildClaimSection(string? pullPaymentLink, long rewardAmountSatoshis)
    {
        if (!string.IsNullOrEmpty(pullPaymentLink))
        {
            var section = $@"1. Click the link below to claim your reward:
{pullPaymentLink}

2. Choose your preferred payout method (on-chain BTC, Lightning, LNURL, or any enabled payout method).
3. Submit the payout details to receive {rewardAmountSatoshis} sats.";

            return section;
        }

        return "A reward was issued but no claim link is available yet. Please contact the store for assistance.";
    }
}
