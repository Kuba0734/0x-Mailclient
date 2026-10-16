using Microsoft.Win32;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace MailClient.App.Services;

/// <summary>
/// "Start with Windows" for both deployment modes:
/// packaged (MSIX) apps use a StartupTask declared in Package.appxmanifest,
/// unpackaged apps use the per-user Run registry key.
/// </summary>
public static class AutostartService
{
    public const string StartupTaskId = "MailClientStartup";
    public const string AutostartArgument = "--autostart";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "MailClient";

    public static bool IsPackaged
    {
        get
        {
            try { return Package.Current != null; }
            catch { return false; }
        }
    }

    public static async Task<bool> IsEnabledAsync()
    {
        try
        {
            if (IsPackaged)
            {
                var task = await StartupTask.GetAsync(StartupTaskId);
                return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    /// <returns>null on success, otherwise a user-facing explanation.</returns>
    public static async Task<string?> SetEnabledAsync(bool enable)
    {
        try
        {
            if (IsPackaged)
            {
                var task = await StartupTask.GetAsync(StartupTaskId);
                if (!enable)
                {
                    task.Disable();
                    return null;
                }

                var state = await task.RequestEnableAsync();
                return state switch
                {
                    StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => null,
                    StartupTaskState.DisabledByUser =>
                        "Autostart wurde in den Windows-Einstellungen deaktiviert. " +
                        "Aktiviere \"Mail\" unter Einstellungen › Apps › Autostart.",
                    StartupTaskState.DisabledByPolicy =>
                        "Autostart ist durch eine Richtlinie deaktiviert.",
                    _ => "Autostart konnte nicht aktiviert werden."
                };
            }

            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            if (enable)
            {
                var exe = Environment.ProcessPath
                    ?? throw new InvalidOperationException("Programmpfad unbekannt.");
                key.SetValue(RunValueName, $"\"{exe}\" {AutostartArgument}");
            }
            else
            {
                key.DeleteValue(RunValueName, false);
            }
            return null;
        }
        catch (Exception ex)
        {
            return $"Autostart konnte nicht geändert werden: {ex.Message}";
        }
    }

    /// <summary>Keeps the registry entry pointing at the current executable (e.g. after an update).</summary>
    public static async Task RefreshRegistrationAsync()
    {
        if (IsPackaged || !SettingsService.Current.StartWithWindows) return;
        if (await IsEnabledAsync())
            await SetEnabledAsync(true);
    }
}
