using System.Net.Sockets;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MailClient.Core.Models;
using MimeKit;

namespace MailClient.Core.Services;

/// <summary>
/// Thread-safe IMAP wrapper. All operations are serialized through a semaphore
/// (an ImapClient can only run one command at a time) and transparently
/// reconnect once if the connection was dropped.
/// </summary>
public sealed class ImapService : IDisposable
{
    private ImapClient _client = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, IMailFolder?> _folders = [];

    private string _host = "", _user = "", _password = "";
    private int _port;
    private bool _ssl;
    private bool _disposed;

    public const string Inbox = "INBOX";
    public const string Sent = "SENT";
    public const string Drafts = "DRAFTS";
    public const string Archive = "ARCHIVE";
    public const string Spam = "SPAM";
    public const string Trash = "TRASH";

    private const MessageSummaryItems SummaryItems =
        MessageSummaryItems.UniqueId |
        MessageSummaryItems.Envelope |
        MessageSummaryItems.Flags |
        MessageSummaryItems.InternalDate |
        MessageSummaryItems.BodyStructure |
        MessageSummaryItems.PreviewText;

    public bool IsConnected => _client.IsConnected && _client.IsAuthenticated;

    public bool IsGmail =>
        _host.Contains("gmail", StringComparison.OrdinalIgnoreCase) ||
        _host.Contains("googlemail", StringComparison.OrdinalIgnoreCase);

    /// <summary>Providers that store a copy in "Sent" themselves when sending via SMTP.</summary>
    public bool ServerSavesSentMail =>
        IsGmail ||
        _host.Contains("office365", StringComparison.OrdinalIgnoreCase) ||
        _host.Contains("outlook", StringComparison.OrdinalIgnoreCase);

    public static SecureSocketOptions GetSocketOptions(int port, bool ssl) =>
        !ssl ? SecureSocketOptions.StartTlsWhenAvailable
        : port is 993 or 465 ? SecureSocketOptions.SslOnConnect
        : SecureSocketOptions.StartTls;

    public bool HasFolder(string tag) =>
        _folders.TryGetValue(tag, out var f) && f != null;

    // ── Connection ────────────────────────────────────────────────────────

    public async Task ConnectAsync(
        string host, int port, bool ssl,
        string username, string password,
        CancellationToken ct = default)
    {
        _host = host; _port = port; _ssl = ssl;
        _user = username; _password = password;

        await _lock.WaitAsync(ct);
        try { await ConnectCoreAsync(ct); }
        finally { _lock.Release(); }
    }

    private async Task ConnectCoreAsync(CancellationToken ct)
    {
        try
        {
            if (_client.IsConnected)
                await _client.DisconnectAsync(true, ct);
        }
        catch { /* stale connection */ }

        _client.Dispose();
        _client = new ImapClient { Timeout = 60_000 };

        await _client.ConnectAsync(_host, _port, GetSocketOptions(_port, _ssl), ct);
        await _client.AuthenticateAsync(_user, _password, ct);
        await ResolveFoldersAsync(ct);
    }

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> op, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lock.WaitAsync(ct);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (!IsConnected)
                        await ConnectCoreAsync(ct);
                    return await op(ct);
                }
                catch (Exception ex) when (attempt == 0 && IsTransient(ex) && !ct.IsCancellationRequested)
                {
                    try { _client.Disconnect(false); } catch { }
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private Task RunAsync(Func<CancellationToken, Task> op, CancellationToken ct) =>
        RunAsync(async c => { await op(c); return true; }, ct);

    private static bool IsTransient(Exception ex) =>
        ex is IOException or SocketException or ServiceNotConnectedException
           or ImapProtocolException or ServiceNotAuthenticatedException;

    // ── Folders ───────────────────────────────────────────────────────────

    private static readonly (string Tag, SpecialFolder Special, FolderAttributes Attr, string[] Names)[] FolderDefs =
    [
        (Sent, SpecialFolder.Sent, FolderAttributes.Sent,
            ["Sent", "Sent Mail", "Sent Items", "Sent Messages", "Gesendet",
             "Gesendete Elemente", "Gesendete Objekte", "Gesendete Nachrichten", "Postausgang"]),
        (Drafts, SpecialFolder.Drafts, FolderAttributes.Drafts,
            ["Drafts", "Draft", "Entwürfe", "Entwurf"]),
        (Archive, SpecialFolder.Archive, FolderAttributes.Archive,
            ["Archive", "Archiv", "Archives"]),
        (Spam, SpecialFolder.Junk, FolderAttributes.Junk,
            ["Spam", "Junk", "Junk E-Mail", "Junk-E-Mail", "Junk Email", "Bulk Mail", "Spamverdacht"]),
        (Trash, SpecialFolder.Trash, FolderAttributes.Trash,
            ["Trash", "Deleted", "Deleted Items", "Deleted Messages", "Papierkorb",
             "Gelöschte Elemente", "Gelöschte Objekte", "Bin"]),
    ];

    private async Task ResolveFoldersAsync(CancellationToken ct)
    {
        _folders.Clear();
        _folders[Inbox] = _client.Inbox;

        IList<IMailFolder> all = [];
        try
        {
            if (_client.PersonalNamespaces.Count > 0)
                all = await _client.GetFoldersAsync(_client.PersonalNamespaces[0], false, ct);
        }
        catch { /* listing not supported */ }

        var hasSpecialUse = (_client.Capabilities &
            (ImapCapabilities.SpecialUse | ImapCapabilities.XList)) != 0;

        foreach (var (tag, special, attr, names) in FolderDefs)
        {
            IMailFolder? folder = null;

            if (hasSpecialUse)
            {
                try { folder = _client.GetFolder(special); } catch { }
                if (folder == null && tag == Archive && IsGmail)
                {
                    try { folder = _client.GetFolder(SpecialFolder.All); } catch { }
                }
            }

            folder ??= all.FirstOrDefault(f => (f.Attributes & attr) != 0);
            folder ??= names
                .Select(n => all.FirstOrDefault(f =>
                    string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(f.FullName, n, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(f => f != null);

            _folders[tag] = folder;
        }
    }

    private IMailFolder GetFolder(string tag) =>
        _folders.TryGetValue(tag, out var f) && f != null
            ? f
            : throw new MailFolderMissingException(tag);

    // ── Reading ───────────────────────────────────────────────────────────

    public Task<(List<MailMessage> Messages, int Total)> GetMessagesAsync(
        string tag, int offset = 0, int count = 50, CancellationToken ct = default) =>
        RunAsync(async c =>
        {
            var folder = GetFolder(tag);
            await folder.OpenAsync(FolderAccess.ReadOnly, c);
            try
            {
                var total = folder.Count;
                var end = total - 1 - offset;
                if (end < 0) return (new List<MailMessage>(), total);
                var start = Math.Max(0, end - count + 1);

                var summaries = await folder.FetchAsync(start, end, SummaryItems, c);
                return (ToMessages(summaries, tag), total);
            }
            finally
            {
                await folder.CloseAsync(false, c);
            }
        }, ct);

    public Task<List<MailMessage>> SearchAsync(
        string tag, string query, int max = 100, CancellationToken ct = default) =>
        RunAsync(async c =>
        {
            var folder = GetFolder(tag);
            await folder.OpenAsync(FolderAccess.ReadOnly, c);
            try
            {
                var q = SearchQuery.SubjectContains(query)
                    .Or(SearchQuery.FromContains(query))
                    .Or(SearchQuery.ToContains(query))
                    .Or(SearchQuery.BodyContains(query));

                var uids = await folder.SearchAsync(q, c);
                if (uids.Count == 0) return new List<MailMessage>();

                var recent = uids.Skip(Math.Max(0, uids.Count - max)).ToList();
                var summaries = await folder.FetchAsync(recent, SummaryItems, c);
                return ToMessages(summaries, tag);
            }
            finally
            {
                await folder.CloseAsync(false, c);
            }
        }, ct);

    public Task<MailMessage> GetMessageBodyAsync(
        string tag, uint uid, bool markAsRead = true, CancellationToken ct = default) =>
        RunAsync(async c =>
        {
            var folder = GetFolder(tag);
            await folder.OpenAsync(markAsRead ? FolderAccess.ReadWrite : FolderAccess.ReadOnly, c);
            try
            {
                var id = new UniqueId(uid);
                var mime = await folder.GetMessageAsync(id, c);

                if (markAsRead)
                {
                    try { await folder.AddFlagsAsync(id, MessageFlags.Seen, true, c); }
                    catch { /* non-critical */ }
                }

                var msg = FromMime(mime, uid, tag);
                msg.IsRead = true;
                return msg;
            }
            finally
            {
                await folder.CloseAsync(false, c);
            }
        }, ct);

    public Task<Dictionary<string, int>> GetUnreadCountsAsync(CancellationToken ct = default) =>
        RunAsync(async c =>
        {
            var result = new Dictionary<string, int>();
            foreach (var tag in new[] { Inbox, Spam, Drafts })
            {
                if (!_folders.TryGetValue(tag, out var f) || f == null) continue;
                try
                {
                    if (tag == Drafts)
                    {
                        await f.StatusAsync(StatusItems.Count, c);
                        result[tag] = f.Count;
                    }
                    else
                    {
                        await f.StatusAsync(StatusItems.Unread, c);
                        result[tag] = f.Unread;
                    }
                }
                catch { /* STATUS not permitted for this folder */ }
            }
            return result;
        }, ct);

    /// <summary>
    /// Returns unread inbox messages with a UID greater than <paramref name="sinceUid"/>
    /// and the highest UID currently in the inbox.
    /// </summary>
    public Task<(List<MailMessage> Messages, uint MaxUid)> GetNewInboxMessagesAsync(
        uint sinceUid, CancellationToken ct = default) =>
        RunAsync(async c =>
        {
            var inbox = GetFolder(Inbox);
            await inbox.OpenAsync(FolderAccess.ReadOnly, c);
            try
            {
                if (inbox.Count == 0) return (new List<MailMessage>(), sinceUid);

                var last = await inbox.FetchAsync(inbox.Count - 1, inbox.Count - 1,
                    MessageSummaryItems.UniqueId, c);
                var maxUid = last.Count > 0 ? last[0].UniqueId.Id : sinceUid;

                if (sinceUid == 0 || maxUid <= sinceUid)
                    return (new List<MailMessage>(), maxUid);

                var range = new UniqueIdRange(new UniqueId(sinceUid + 1), UniqueId.MaxValue);
                var uids = (await inbox.SearchAsync(
                        SearchQuery.Uids(range).And(SearchQuery.NotSeen), c))
                    .Where(u => u.Id > sinceUid)
                    .ToList();

                if (uids.Count == 0) return (new List<MailMessage>(), maxUid);

                var summaries = await inbox.FetchAsync(uids, SummaryItems, c);
                return (ToMessages(summaries, Inbox), maxUid);
            }
            finally
            {
                await inbox.CloseAsync(false, c);
            }
        }, ct);

    // ── Modifying ─────────────────────────────────────────────────────────

    public Task SetFlagAsync(string tag, uint uid, MessageFlags flag, bool set,
        CancellationToken ct = default) =>
        RunAsync(async c =>
        {
            var folder = GetFolder(tag);
            await folder.OpenAsync(FolderAccess.ReadWrite, c);
            try
            {
                var id = new UniqueId(uid);
                if (set) await folder.AddFlagsAsync(id, flag, true, c);
                else await folder.RemoveFlagsAsync(id, flag, true, c);
            }
            finally
            {
                await folder.CloseAsync(false, c);
            }
        }, ct);

    public Task MarkAllReadAsync(string tag, CancellationToken ct = default) =>
        RunAsync(async c =>
        {
            var folder = GetFolder(tag);
            await folder.OpenAsync(FolderAccess.ReadWrite, c);
            try
            {
                var unseen = await folder.SearchAsync(SearchQuery.NotSeen, c);
                if (unseen.Count > 0)
                    await folder.AddFlagsAsync(unseen, MessageFlags.Seen, true, c);
            }
            finally
            {
                await folder.CloseAsync(false, c);
            }
        }, ct);

    public Task MoveAsync(string tag, uint uid, string targetTag, CancellationToken ct = default) =>
        RunAsync(async c =>
        {
            var source = GetFolder(tag);
            var target = GetFolder(targetTag);
            await source.OpenAsync(FolderAccess.ReadWrite, c);
            try
            {
                await source.MoveToAsync(new UniqueId(uid), target, c);
            }
            finally
            {
                await source.CloseAsync(true, c);
            }
        }, ct);

    /// <summary>
    /// Moves the message to the trash. Messages that already are in the trash
    /// (or when no trash folder exists) are deleted permanently.
    /// </summary>
    /// <returns>true if the message was deleted permanently.</returns>
    public Task<bool> DeleteAsync(string tag, uint uid, CancellationToken ct = default) =>
        RunAsync(async c =>
        {
            var source = GetFolder(tag);
            var permanent = tag == Trash || !HasFolder(Trash);

            await source.OpenAsync(FolderAccess.ReadWrite, c);
            try
            {
                var id = new UniqueId(uid);
                if (permanent)
                {
                    await source.AddFlagsAsync(id, MessageFlags.Deleted, true, c);
                    if (_client.Capabilities.HasFlag(ImapCapabilities.UidPlus))
                        await source.ExpungeAsync([id], c);
                    else
                        await source.ExpungeAsync(c);
                }
                else
                {
                    await source.MoveToAsync(id, GetFolder(Trash), c);
                }
            }
            finally
            {
                await source.CloseAsync(false, c);
            }
            return permanent;
        }, ct);

    /// <summary>Deletes a message permanently (e.g. a replaced draft).</summary>
    public Task DeletePermanentAsync(string tag, uint uid, CancellationToken ct = default) =>
        RunAsync(async c =>
        {
            var folder = GetFolder(tag);
            await folder.OpenAsync(FolderAccess.ReadWrite, c);
            try
            {
                var id = new UniqueId(uid);
                await folder.AddFlagsAsync(id, MessageFlags.Deleted, true, c);
                if (_client.Capabilities.HasFlag(ImapCapabilities.UidPlus))
                    await folder.ExpungeAsync([id], c);
                else
                    await folder.ExpungeAsync(c);
            }
            finally
            {
                await folder.CloseAsync(false, c);
            }
        }, ct);

    public Task AppendAsync(string tag, MimeMessage message, MessageFlags flags,
        CancellationToken ct = default) =>
        RunAsync(async c =>
        {
            var folder = GetFolder(tag);
            await folder.AppendAsync(new AppendRequest(message, flags), c);
        }, ct);

    // ── Mapping ───────────────────────────────────────────────────────────

    private static List<MailMessage> ToMessages(IList<IMessageSummary> summaries, string tag) =>
        summaries
            .Select(s =>
            {
                var env = s.Envelope;
                var sender = env?.From.Mailboxes.FirstOrDefault();
                var date = env?.Date ?? s.InternalDate ?? DateTimeOffset.MinValue;
                var flags = s.Flags ?? MessageFlags.None;

                return new MailMessage
                {
                    Uid = s.UniqueId.Id,
                    FolderTag = tag,
                    Subject = string.IsNullOrWhiteSpace(env?.Subject) ? "(Kein Betreff)" : env!.Subject,
                    From = env?.From.ToString() ?? "",
                    FromAddress = sender?.Address ?? "",
                    FromDisplay = !string.IsNullOrWhiteSpace(sender?.Name) ? sender!.Name
                        : sender?.Address ?? "(Unbekannt)",
                    ReplyTo = env?.ReplyTo.ToString() ?? "",
                    To = env?.To.ToString() ?? "",
                    Cc = env?.Cc.ToString() ?? "",
                    Date = date.LocalDateTime,
                    MessageId = env?.MessageId ?? "",
                    PreviewText = s.PreviewText ?? "",
                    IsRead = flags.HasFlag(MessageFlags.Seen),
                    IsFlagged = flags.HasFlag(MessageFlags.Flagged),
                    IsDraft = flags.HasFlag(MessageFlags.Draft) || tag == Drafts,
                    HasAttachments = s.Attachments?.Any() == true
                };
            })
            .OrderByDescending(m => m.Date)
            .ToList();

    public static MailMessage FromMime(MimeMessage mime, uint uid, string tag)
    {
        var sender = mime.From.Mailboxes.FirstOrDefault();

        var attachments = mime.Attachments
            .Select(a => new MailAttachment
            {
                FileName = a switch
                {
                    MimePart p when !string.IsNullOrEmpty(p.FileName) => p.FileName,
                    MessagePart m => (m.Message?.Subject ?? "Nachricht") + ".eml",
                    _ => "Anhang"
                },
                ContentType = a.ContentType.MimeType,
                Size = EstimateSize(a),
                Entity = a
            })
            .ToList();

        return new MailMessage
        {
            Uid = uid,
            FolderTag = tag,
            Subject = string.IsNullOrWhiteSpace(mime.Subject) ? "(Kein Betreff)" : mime.Subject,
            From = mime.From.ToString(),
            FromAddress = sender?.Address ?? "",
            FromDisplay = !string.IsNullOrWhiteSpace(sender?.Name) ? sender!.Name : sender?.Address ?? "",
            ReplyTo = mime.ReplyTo.ToString(),
            To = mime.To.ToString(),
            Cc = mime.Cc.ToString(),
            Date = mime.Date.LocalDateTime,
            HtmlBody = InlineCidImages(mime),
            TextBody = mime.TextBody ?? "",
            MessageId = mime.MessageId ?? "",
            References = [.. mime.References],
            Attachments = attachments,
            HasAttachments = attachments.Count > 0,
            IsDraft = tag == Drafts,
            Mime = mime
        };
    }

    private static long EstimateSize(MimeEntity entity)
    {
        if (entity is MimePart { Content: not null } part)
        {
            try
            {
                var len = part.Content!.Stream?.Length ?? 0;
                // base64 inflates by ~4/3
                return part.ContentTransferEncoding == ContentEncoding.Base64 ? len * 3 / 4 : len;
            }
            catch { }
        }
        return 0;
    }

    private static string InlineCidImages(MimeMessage mime)
    {
        var html = mime.HtmlBody ?? "";
        if (html.Length == 0 || !html.Contains("cid:", StringComparison.OrdinalIgnoreCase))
            return html;

        foreach (var part in mime.BodyParts.OfType<MimePart>())
        {
            if (string.IsNullOrEmpty(part.ContentId) || part.Content == null) continue;
            try
            {
                using var ms = new MemoryStream();
                part.Content.DecodeTo(ms);
                var data = $"data:{part.ContentType.MimeType};base64,{Convert.ToBase64String(ms.ToArray())}";
                html = html.Replace($"cid:{part.ContentId}", data, StringComparison.OrdinalIgnoreCase);
            }
            catch { /* skip broken part */ }
        }
        return html;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
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

public sealed class MailFolderMissingException(string tag)
    : Exception($"Der Ordner \"{tag}\" existiert auf diesem Server nicht.")
{
    public string Tag { get; } = tag;
}
