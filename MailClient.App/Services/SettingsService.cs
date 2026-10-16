using System;
using System.IO;
using System.Text.Json;

namespace MailClient.App.Services;

public class AppSettings
{
    public string Theme { get; set; } = "System";

    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool MinimizeToTray { get; set; }
    public bool TrayHintShown { get; set; }

    public bool Notifications { get; set; } = true;
    public int SyncIntervalMinutes { get; set; } = 5;

    public Guid? LastAccountId { get; set; }
    public bool PaneCollapsed { get; set; }

    public int WindowX { get; set; } = int.MinValue;
    public int WindowY { get; set; } = int.MinValue;
    public int WindowWidth { get; set; } = 1440;
    public int WindowHeight { get; set; } = 900;
    public bool WindowMaximized { get; set; }
}

public static class SettingsService
{
    private static string _path = "";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Current { get; private set; } = new();

    /// <summary>Raised after <see cref="Save"/> was called.</summary>
    public static event Action? Changed;

    public static void Load(string dataDir)
    {
        _path = Path.Combine(dataDir, "settings.json");
        try
        {
            if (File.Exists(_path))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new();
        }
        catch
        {
            Current = new();
        }
    }

    public static void Save(bool notify = true)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch { /* settings are not critical */ }

        if (notify) Changed?.Invoke();
    }
}
