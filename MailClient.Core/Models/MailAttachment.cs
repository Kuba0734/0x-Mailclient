using System.Text.Json.Serialization;
using MimeKit;

namespace MailClient.Core.Models;

public class MailAttachment
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }

    /// <summary>Local file path (outgoing attachments only).</summary>
    public string? LocalPath { get; set; }

    [JsonIgnore]
    public MimeEntity? Entity { get; set; }

    public string SizeDisplay => FormatSize(Size);

    public string Extension
    {
        get
        {
            var ext = Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant();
            return ext.Length is > 0 and <= 4 ? ext : "FILE";
        }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        <= 0 => "",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.0} MB"
    };

    /// <summary>Writes the decoded attachment content to <paramref name="target"/>.</summary>
    public async Task SaveToAsync(Stream target, CancellationToken ct = default)
    {
        if (LocalPath != null)
        {
            await using var fs = File.OpenRead(LocalPath);
            await fs.CopyToAsync(target, ct).ConfigureAwait(false);
            return;
        }

        switch (Entity)
        {
            case MimePart { Content: not null } part:
                await part.Content.DecodeToAsync(target, ct).ConfigureAwait(false);
                break;
            case MessagePart { Message: not null } msgPart:
                await msgPart.Message.WriteToAsync(target, ct).ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException("Anhang ist nicht verfügbar.");
        }
    }
}
