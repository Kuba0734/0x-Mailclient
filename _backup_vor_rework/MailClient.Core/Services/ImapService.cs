using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MailClient.Core.Models;

namespace MailClient.Core.Services;

public class ImapService : IDisposable
{
    private ImapClient _client = new();

    public bool IsConnected => _client.IsConnected && _client.IsAuthenticated;

    public async Task ConnectAsync(
        string host, int port, bool ssl,
        string username, string password,
        CancellationToken ct = default)
    {
        if (_client.IsConnected)
            _client.Disconnect(true);

        var options = ssl
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTlsWhenAvailable;

        await _client.ConnectAsync(host, port, options, ct);
        await _client.AuthenticateAsync(username, password, ct);
    }

    public async Task<List<MailMessage>> GetMessagesAsync(
        string folderTag, int count = 50,
        CancellationToken ct = default)
    {
        var folder = await ResolveFolder(folderTag, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        var messages = new List<MailMessage>();
        var total = folder.Count;

        if (total > 0)
        {
            var start = Math.Max(0, total - count);
            var summaries = await folder.FetchAsync(
                start, total - 1,
                MessageSummaryItems.UniqueId |
                MessageSummaryItems.Envelope |
                MessageSummaryItems.Flags |
                MessageSummaryItems.BodyStructure,
                ct);

            foreach (var s in summaries.OrderByDescending(m => m.Date))
            {
                messages.Add(new MailMessage
                {
                    Uid = s.UniqueId.Id,
                    Subject = s.Envelope.Subject ?? "(Kein Betreff)",
                    From = s.Envelope.From.ToString(),
                    FromDisplay = s.Envelope.From.Mailboxes
                        .FirstOrDefault()?.Name ?? s.Envelope.From.ToString(),
                    To = s.Envelope.To.ToString(),
                    Cc = s.Envelope.Cc.ToString(),
                    Date = s.Date.LocalDateTime,
                    IsRead = s.Flags.HasValue &&
                        (s.Flags.Value & MessageFlags.Seen) != 0,
                    IsFlagged = s.Flags.HasValue &&
                        (s.Flags.Value & MessageFlags.Flagged) != 0,
                    HasAttachments = s.Attachments.Any()
                });
            }
        }

        await folder.CloseAsync(false, ct);
        return messages;
    }

    public async Task<MailMessage> GetMessageBodyAsync(
        string folderTag, uint uid,
        CancellationToken ct = default)
    {
        var folder = await ResolveFolder(folderTag, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);

        var mimeMsg = await folder.GetMessageAsync(new UniqueId(uid), ct);

        // Mark as read
        try
        {
            await folder.AddFlagsAsync(
                new UniqueId(uid), MessageFlags.Seen, true, ct);
        }
        catch { /* non-critical */ }

        await folder.CloseAsync(true, ct);

        return new MailMessage
        {
            Uid = uid,
            Subject = mimeMsg.Subject ?? "(Kein Betreff)",
            From = mimeMsg.From.ToString(),
            FromDisplay = mimeMsg.From.Mailboxes
                .FirstOrDefault()?.Name ?? mimeMsg.From.ToString(),
            To = mimeMsg.To.ToString(),
            Cc = mimeMsg.Cc.ToString(),
            Date = mimeMsg.Date.LocalDateTime,
            HtmlBody = mimeMsg.HtmlBody ?? "",
            TextBody = mimeMsg.TextBody ?? "",
            IsRead = true,
            HasAttachments = mimeMsg.Attachments.Any()
        };
    }

    public async Task<int> GetUnreadCountAsync(
        string folderTag, CancellationToken ct = default)
    {
        try
        {
            var folder = await ResolveFolder(folderTag, ct);
            await folder.OpenAsync(FolderAccess.ReadOnly, ct);
            var count = folder.Unread;
            await folder.CloseAsync(false, ct);
            return count;
        }
        catch { return 0; }
    }

    private async Task<IMailFolder> ResolveFolder(
        string folderTag, CancellationToken ct)
    {
        if (folderTag == "INBOX")
            return _client.Inbox;

        // Try SpecialFolder enum first
        var specialMap = new Dictionary<string, SpecialFolder>
        {
            ["SENT"] = SpecialFolder.Sent,
            ["DRAFTS"] = SpecialFolder.Drafts,
            ["TRASH"] = SpecialFolder.Trash,
            ["SPAM"] = SpecialFolder.Junk,
            ["ARCHIVE"] = SpecialFolder.Archive
        };

        if (specialMap.TryGetValue(folderTag, out var special))
        {
            try
            {
                var sf = _client.GetFolder(special);
                if (sf != null) return sf;
            }
            catch { /* fall through to name-based lookup */ }
        }

        // Fallback: search by name
        var nameMap = new Dictionary<string, string[]>
        {
            ["SENT"] = ["Sent", "Sent Mail", "Gesendet", "[Gmail]/Sent Mail"],
            ["DRAFTS"] = ["Drafts", "Entwürfe", "[Gmail]/Drafts"],
            ["TRASH"] = ["Trash", "Deleted", "Papierkorb", "[Gmail]/Trash"],
            ["SPAM"] = ["Spam", "Junk", "[Gmail]/Spam"]
        };

        if (nameMap.TryGetValue(folderTag, out var names))
        {
            try
            {
                var allFolders = await _client.GetFoldersAsync(
                    _client.PersonalNamespaces[0], false, ct);

                foreach (var name in names)
                {
                    var match = allFolders.FirstOrDefault(f =>
                        string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(f.FullName, name, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return match;
                }
            }
            catch { /* fall through */ }
        }

        return _client.Inbox;
    }

    public void Dispose()
    {
        try
        {
            if (_client.IsConnected)
                lock (_client.SyncRoot)
                    _client.Disconnect(true);
        }
        catch { /* ignore races on dispose */ }
        _client.Dispose();
    }
}
