using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MailClient.Core.Services;

public class SmtpService
{
    public async Task SendAsync(
        string fromEmail, string fromName,
        string to, string cc, string subject,
        string htmlBody, string textBody,
        string smtpHost, int smtpPort, bool ssl,
        string username, string password,
        CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromEmail));

        foreach (var addr in to.Split([',', ';'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            message.To.Add(MailboxAddress.Parse(addr));
        }

        if (!string.IsNullOrWhiteSpace(cc))
        {
            foreach (var addr in cc.Split([',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                message.Cc.Add(MailboxAddress.Parse(addr));
            }
        }

        message.Subject = subject;

        var builder = new BodyBuilder();
        if (!string.IsNullOrEmpty(htmlBody))
            builder.HtmlBody = htmlBody;
        if (!string.IsNullOrEmpty(textBody))
            builder.TextBody = textBody;
        else if (string.IsNullOrEmpty(htmlBody))
            builder.TextBody = "";

        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        var options = ssl
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTlsWhenAvailable;

        await client.ConnectAsync(smtpHost, smtpPort, options, ct);
        await client.AuthenticateAsync(username, password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
