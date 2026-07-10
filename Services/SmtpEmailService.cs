using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Clicky.Windows.Services;

public sealed class SmtpEmailService
{
    public async Task SendAsync(
        EmailSettings settings,
        string? password,
        AgentEmailProposal proposal,
        CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException("Configure SMTP settings before sending email.");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            string.IsNullOrWhiteSpace(settings.FromName) ? settings.FromAddress : settings.FromName.Trim(),
            settings.FromAddress.Trim()));
        message.To.AddRange(proposal.To.Select(MailboxAddress.Parse));
        message.Cc.AddRange(proposal.Cc.Select(MailboxAddress.Parse));
        message.Subject = proposal.Subject;
        message.Body = new TextPart("plain") { Text = proposal.Body };

        using var client = new SmtpClient { Timeout = 60_000 };
        await client.ConnectAsync(
            settings.SmtpHost.Trim(),
            settings.SmtpPort,
            ToSecureSocketOptions(settings.Security),
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("The SMTP account needs a saved password or app password.");
            }
            await client.AuthenticateAsync(settings.Username.Trim(), password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    private static SecureSocketOptions ToSecureSocketOptions(SmtpSecurityMode mode) => mode switch
    {
        SmtpSecurityMode.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurityMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
        SmtpSecurityMode.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.Auto
    };
}
