using MailClient.Core.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Utils;

namespace MailClient.Core.Services;

public class OutgoingMail
{
    public string To { get; set; } = "";
    public string Cc { get; set; } = "";
    public string Bcc { get; set; } = "";
    public string Subject { get; set; } = "";
    public string TextBody { get; set; } = "";
    public string? InReplyTo { get; set; }
    public List<string> References { get; set; } = [];
    public List<string> AttachmentPaths { get; set; } = [];

    /// <summary>Attachments taken over from an existing message (e.g. forwarding).</summary>
    public List<MailAttachment> ForwardedAttachments { get; set; } = [];
}

public class SmtpService
{
    public static SecureSocketOptions GetSocketOptions(int port, bool ssl) =>
        !ssl ? SecureSocketOptions.StartTlsWhenAvailable
        : port == 465 ? SecureSocketOptions.SslOnConnect
        : SecureSocketOptions.StartTls;

    public static InternetAddressList ParseAddresses(string text, string fieldName)
    {
        var list = new InternetAddressList();
        if (string.IsNullOrWhiteSpace(text)) return list;

        var normalized = text.Replace(';', ',').Trim().TrimEnd(',');
        if (!InternetAddressList.TryParse(normalized, out var parsed) || parsed.Count == 0)
            throw new FormatException($"Ungültige Adresse im Feld \"{fieldName}\": {text}");

        foreach (var mb in parsed.Mailboxes)
        {
            if (!mb.Address.Contains('@'))
                throw new FormatException($"Ungültige Adresse im Feld \"{fieldName}\": {mb.Address}");
        }
        list.AddRange(parsed);
        return list;
    }

    public static MimeMessage BuildMessage(MailAccount account, OutgoingMail mail)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(account.DisplayName, account.EmailAddress));
        message.To.AddRange(ParseAddresses(mail.To, "An"));
        message.Cc.AddRange(ParseAddresses(mail.Cc, "CC"));
        message.Bcc.AddRange(ParseAddresses(mail.Bcc, "BCC"));
        message.Subject = mail.Subject;
        message.Date = DateTimeOffset.Now;

        var domain = account.EmailAddress.Contains('@')
            ? account.EmailAddress[(account.EmailAddress.IndexOf('@') + 1)..]
            : "localhost";
        message.MessageId = MimeUtils.GenerateMessageId(domain);

        if (!string.IsNullOrEmpty(mail.InReplyTo))
            message.InReplyTo = mail.InReplyTo;
        foreach (var r in mail.References)
            message.References.Add(r);

        var builder = new BodyBuilder
        {
            TextBody = mail.TextBody,
            HtmlBody = TextToHtml(mail.TextBody)
        };

        foreach (var path in mail.AttachmentPaths)
            builder.Attachments.Add(path);

        foreach (var att in mail.ForwardedAttachments)
        {
            using var ms = new MemoryStream();
            att.SaveToAsync(ms).GetAwaiter().GetResult();
            builder.Attachments.Add(att.FileName, ms.ToArray(),
                ContentType.TryParse(att.ContentType, out var ctype) ? ctype : new ContentType("application", "octet-stream"));
        }

        message.Body = builder.ToMessageBody();
        return message;
    }

    private static string TextToHtml(string text)
    {
        var encoded = System.Net.WebUtility.HtmlEncode(text)
            .Replace("\r\n", "\n")
            .Replace("\n", "<br>");
        return $"<div style=\"font-family:'Segoe UI',Arial,sans-serif;font-size:14px;line-height:1.5\">{encoded}</div>";
    }

    public async Task SendAsync(
        MailAccount account, string password, MimeMessage message,
        CancellationToken ct = default)
    {
        using var client = new SmtpClient { Timeout = 60_000 };
        await client.ConnectAsync(account.SmtpHost, account.SmtpPort,
            GetSocketOptions(account.SmtpPort, account.SmtpUseSsl), ct);
        await client.AuthenticateAsync(account.Username, password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
