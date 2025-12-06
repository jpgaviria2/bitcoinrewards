#nullable enable
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Newtonsoft.Json;

// Shim for BTCPayServer.Plugins.Emails.Services.EmailSettings so we can read
// server SMTP settings even when the Emails plugin assembly is not present.
namespace BTCPayServer.Plugins.Emails.Services;

public class EmailSettings
{
    public string Server { get; set; } = string.Empty;
    public int? Port { get; set; }
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public bool DisableCertificateCheck { get; set; }

    [JsonIgnore]
    public bool EnabledCertificateCheck
    {
        get => !DisableCertificateCheck;
        set { DisableCertificateCheck = !value; }
    }

    public bool IsComplete()
    {
        return MailboxAddressValidator.IsMailboxAddress(From)
               && !string.IsNullOrWhiteSpace(Server)
               && Port != null;
    }

    public MimeMessage CreateMailMessage(MailboxAddress to, string subject, string message, bool isHtml)
        => CreateMailMessage(new[] { to }, null, null, subject, message, isHtml);

    public MimeMessage CreateMailMessage(MailboxAddress[] to, MailboxAddress[]? cc, MailboxAddress[]? bcc, string subject, string message, bool isHtml)
    {
        var bodyBuilder = new BodyBuilder();
        if (isHtml)
            bodyBuilder.HtmlBody = message;
        else
            bodyBuilder.TextBody = message;

        var mm = new MimeMessage();
        mm.Body = bodyBuilder.ToMessageBody();
        mm.Subject = subject;
        mm.From.Add(MailboxAddressValidator.Parse(From));
        mm.To.AddRange(to);
        mm.Cc.AddRange(cc ?? System.Array.Empty<InternetAddress>());
        mm.Bcc.AddRange(bcc ?? System.Array.Empty<InternetAddress>());
        return mm;
    }

    public async Task<SmtpClient> CreateSmtpClient()
    {
        var client = new SmtpClient();
        using var connectCancel = new CancellationTokenSource(10000);
        try
        {
            if (DisableCertificateCheck)
            {
                client.CheckCertificateRevocation = false;
#pragma warning disable CA5359 // Do Not Disable Certificate Validation
                client.ServerCertificateValidationCallback = (s, c, h, e) => true;
#pragma warning restore CA5359
            }
            await client.ConnectAsync(Server, Port!.Value, SecureSocketOptions.Auto, connectCancel.Token);
            if ((client.Capabilities & SmtpCapabilities.Authentication) != 0)
            {
                await client.AuthenticateAsync(Login ?? string.Empty, Password ?? string.Empty, connectCancel.Token);
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }
        return client;
    }
}

// Minimal validator copies to avoid pulling the full Emails plugin
public static class MailboxAddressValidator
{
    public static bool IsMailboxAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        try
        {
            Parse(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static MailboxAddress Parse(string value) => MailboxAddress.Parse(value);
}

