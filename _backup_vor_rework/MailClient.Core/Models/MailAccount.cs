namespace MailClient.Core.Models;

public class MailAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = "";
    public string EmailAddress { get; set; } = "";
    public string Username { get; set; } = "";

    // IMAP
    public string ImapHost { get; set; } = "";
    public int ImapPort { get; set; } = 993;
    public bool ImapUseSsl { get; set; } = true;

    // SMTP
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;

    public override string ToString() =>
        DisplayName.Length > 0 ? DisplayName : EmailAddress;
}
