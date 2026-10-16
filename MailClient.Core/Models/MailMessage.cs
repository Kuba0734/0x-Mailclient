using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MimeKit;

namespace MailClient.Core.Models;

public class MailMessage : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public uint Uid { get; set; }
    public string FolderTag { get; set; } = "INBOX";
    public string Subject { get; set; } = "(Kein Betreff)";

    /// <summary>Full RFC address list, e.g. "Max Muster &lt;max@example.com&gt;".</summary>
    public string From { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromDisplay { get; set; } = "";
    public string ReplyTo { get; set; } = "";
    public string To { get; set; } = "";
    public string Cc { get; set; } = "";
    public DateTime Date { get; set; }

    public string HtmlBody { get; set; } = "";
    public string TextBody { get; set; } = "";
    public string PreviewText { get; set; } = "";

    public string MessageId { get; set; } = "";
    public List<string> References { get; set; } = [];
    public List<MailAttachment> Attachments { get; set; } = [];

    public bool HasAttachments { get; set; }
    public bool IsDraft { get; set; }

    [JsonIgnore]
    public MimeMessage? Mime { get; set; }

    private bool _isRead;
    public bool IsRead
    {
        get => _isRead;
        set => Set(ref _isRead, value);
    }

    private bool _isFlagged;
    public bool IsFlagged
    {
        get => _isFlagged;
        set => Set(ref _isFlagged, value);
    }

    public string Preview
    {
        get
        {
            var text = PreviewText.Length > 0 ? PreviewText
                : TextBody.Length > 0 ? TextBody
                : Regex.Replace(HtmlBody, "<[^>]*>", " ");
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return text.Length > 220 ? text[..220] : text;
        }
    }

    /// <summary>Plain-text representation of the body (falls back to stripped HTML).</summary>
    public string PlainBody
    {
        get
        {
            if (TextBody.Length > 0) return TextBody;
            if (HtmlBody.Length == 0) return "";
            var t = Regex.Replace(HtmlBody, @"<(script|style)[^>]*>.*?</\1>", "",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"<br\s*/?>|</p>|</div>|</tr>|</h\d>", "\n", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "<[^>]*>", "");
            t = System.Net.WebUtility.HtmlDecode(t);
            t = Regex.Replace(t, @"\n{3,}", "\n\n");
            return t.Trim();
        }
    }

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
