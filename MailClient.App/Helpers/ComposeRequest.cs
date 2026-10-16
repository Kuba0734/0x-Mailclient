using MailClient.Core.Models;
using System.Collections.Generic;

namespace MailClient.App.Helpers;

public sealed class ComposeRequest
{
    public string Title { get; set; } = "Neue Nachricht";
    public string To { get; set; } = "";
    public string Cc { get; set; } = "";
    public string Bcc { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string? InReplyTo { get; set; }
    public List<string> References { get; set; } = [];
    public List<MailAttachment> Attachments { get; set; } = [];

    /// <summary>UID of the draft that is being edited (replaced on save/send).</summary>
    public uint? DraftUid { get; set; }

    /// <summary>Put the caret into the body instead of the "To" field.</summary>
    public bool FocusBody { get; set; }
}
