using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MailClient.App.Services;

/// <summary>
/// Self-update from GitHub releases. A release (created by deploy.ps1) carries
/// <see cref="AssetName"/> plus a ".sha256" file; the app downloads it, verifies the hash,
/// and swaps its own files via a small script that runs after the app has exited.
/// </summary>
public static class UpdateService
{
    private const string Repo = "Kuba0734/0x-Mailclient";
    public const string AssetName = "MailClient-win-x64.zip";

    public sealed record UpdateInfo(Version Version, string Tag, string ZipUrl, string ShaUrl);

    public static Version Current => Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0));

    /// <summary>
    /// Only the unpackaged release build can replace its own files
    /// (MSIX installs are read-only, debug builds must never update themselves).
    /// </summary>
    public static bool CanSelfUpdate
    {
        get
        {
#if DEBUG
            return false;
#else
            return !AutostartService.IsPackaged;
#endif
        }
    }

    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    public static bool TryParseTag(string tag, out Version version)
    {
        var ok = Version.TryParse(tag.Trim().TrimStart('v', 'V'), out var v);
        version = ok ? Normalize(v!) : new Version(0, 0, 0, 0);
        return ok;
    }

    private static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromMinutes(10),
        DefaultRequestHeaders = { { "User-Agent", "0x-Mailclient-Updater" } }
    };

    /// <returns>The newer release, or null if the app is up to date.</returns>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var http = CreateClient();
        http.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

        using var response = await http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // no release yet
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!TryParseTag(tag, out var version) || version <= Current) return null;

        string? Url(string name) => root.GetProperty("assets").EnumerateArray()
            .Where(a => a.GetProperty("name").GetString() == name)
            .Select(a => a.GetProperty("browser_download_url").GetString())
            .FirstOrDefault();

        var zip = Url(AssetName);
        var sha = Url(AssetName + ".sha256");
        return zip != null && sha != null ? new UpdateInfo(version, tag, zip, sha) : null;
    }

    /// <summary>Downloads, verifies (SHA-256) and extracts the update. Returns the folder with the new files.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "MailClientUpdate", info.Tag);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, AssetName);

        using var http = CreateClient();
        await using (var src = await http.GetStreamAsync(info.ZipUrl, ct))
        await using (var dst = File.Create(zipPath))
            await src.CopyToAsync(dst, ct);

        var expected = (await http.GetStringAsync(info.ShaUrl, ct)).Trim().Split(' ', '\t')[0];
        string actual;
        await using (var fs = File.OpenRead(zipPath))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Prüfsumme des Updates stimmt nicht – Update verworfen.");

        var files = Path.Combine(dir, "files");
        ZipFile.ExtractToDirectory(zipPath, files);
        if (!File.Exists(Path.Combine(files, "MailClient.App.exe")))
            throw new InvalidDataException("Update-Paket ist unvollständig.");
        return files;
    }

    /// <summary>Starts the swap script and exits; the script restarts the app when done.</summary>
    public static void ApplyAndRestart(string filesDir)
    {
        var target = AppContext.BaseDirectory.TrimEnd('\\');
        var exe = Environment.ProcessPath ?? Path.Combine(target, "MailClient.App.exe");
        var pid = Environment.ProcessId;
        var script = Path.Combine(Path.GetDirectoryName(filesDir)!, "apply.cmd");

        // robocopy exit codes < 8 mean success; the WebView2 cache next to the exe is left alone.
        File.WriteAllText(script, $"""
            @echo off
            chcp 65001 >nul
            :wait
            tasklist /FI "PID eq {pid}" 2>nul | find "{pid}" >nul && (timeout /t 1 /nobreak >nul & goto wait)
            robocopy "{filesDir}" "{target}" /E /R:10 /W:1 /NFL /NDL /NJH /NJS /NP >nul
            start "" "{exe}"
            """, new UTF8Encoding(false));

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        App.Instance?.ExitApplication();
    }
}
