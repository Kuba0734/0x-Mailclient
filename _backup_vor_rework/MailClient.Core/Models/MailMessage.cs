using System.Text.RegularExpressions;

namespace MailClient.Core.Models;

public class MailMessage
{
    public uint Uid { get; set; }
    public string Subject { get; set; } = "(Kein Betreff)";
    public string From { get; set; } = "";
    public string FromDisplay { get; set; } = "";
    public string To { get; set; } = "";
    public string Cc { get; set; } = "";
    public DateTime Date { get; set; }
    public string HtmlBody { get; set; } = "";
    public string TextBody { get; set; } = "";
    public bool IsRead { get; set; }
    public bool IsFlagged { get; set; }
    public bool HasAttachments { get; set; }

    public string Preview
    {
        get
        {
            var text = TextBody.Length > 0
                ? TextBody
                : Regex.Replace(HtmlBody, "<[^>]*>", "");
            return text.Replace("\r\n", " ").Replace("\n", " ").Trim();
        }
    }
}
