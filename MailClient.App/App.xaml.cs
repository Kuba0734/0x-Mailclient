using MailClient.App.Helpers;
using MailClient.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.IO;
using System.Linq;

namespace MailClient.App;

public partial class App : Application
{
    public static App? Instance { get; private set; }
    public static AccountService Accounts { get; private set; } = null!;
    public static string DataDir { get; private set; } = "";

    public static MainWindow MainWindowInstance => Instance!._window!;

    public TrayService Tray { get; } = new();

    private MainWindow? _window;
    private DispatcherQueue? _dispatcher;
    private bool _isExiting;

    public App()
    {
        Instance = this;
        InitializeComponent();

        try
        {
            DataDir = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch
        {
            DataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MailClient");
            Directory.CreateDirectory(DataDir);
            MigrateFromPackagedInstall(DataDir);
        }

        Accounts = new AccountService(DataDir);
        SettingsService.Load(DataDir);

        UnhandledException += (_, e) =>
        {
            // Keep the app alive (it lives in the tray) and surface the error instead.
            e.Handled = true;
            _window?.Shell.ShowToast($"Unerwarteter Fehler: {e.Exception.Message}", isError: true);
        };
    }

    public bool IsExiting => _isExiting;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _window = new MainWindow();

        Tray.OpenRequested += ShowMainWindow;
        Tray.ComposeRequested += () =>
        {
            ShowMainWindow();
            _dispatcher.TryEnqueue(() => _window.Shell.OpenNewCompose());
        };
        Tray.SyncRequested += () => _dispatcher.TryEnqueue(() => _ = _window.Shell.SyncNowAsync());
        Tray.ExitRequested += () => _dispatcher.TryEnqueue(ExitApplication);
        Tray.Create();

        NotificationService.Initialize();
        _ = AutostartService.RefreshRegistrationAsync();
        StartUpdateChecks();

        if (IsAutostartLaunch() && SettingsService.Current.StartMinimized)
        {
            // Stay in the tray; the window is created but never shown.
            return;
        }

        _window.ShowAndActivate();

    }

    /// <summary>Accounts/settings from an earlier MSIX install are taken over once.</summary>
    private static void MigrateFromPackagedInstall(string dataDir)
    {
        try
        {
            if (File.Exists(Path.Combine(dataDir, "accounts.json"))) return;
            var packages = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            var old = Directory.EnumerateDirectories(packages, "MailClient.App_*")
                .Select(d => Path.Combine(d, "LocalState"))
                .FirstOrDefault(d => File.Exists(Path.Combine(d, "accounts.json")));
            if (old == null) return;
            foreach (var name in new[] { "accounts.json", "settings.json" })
            {
                var src = Path.Combine(old, name);
                if (File.Exists(src)) File.Copy(src, Path.Combine(dataDir, name));
            }
        }
        catch { /* migration is best effort */ }
    }

    // ── Updates ─────────────────────────────────────────────────────────

    private DispatcherQueueTimer? _updateTimer;
    private bool _updating;

    private void StartUpdateChecks()
    {
        if (!UpdateService.CanSelfUpdate || _updateTimer != null) return;
        _updateTimer = _dispatcher!.CreateTimer();
        _updateTimer.Interval = TimeSpan.FromSeconds(5); // first check shortly after start
        _updateTimer.Tick += async (t, _) =>
        {
            t.Interval = TimeSpan.FromHours(6);
            await CheckForUpdateAsync(manual: false);
        };
        _updateTimer.Start();
    }

    /// <returns>A short status text for the settings page.</returns>
    public async System.Threading.Tasks.Task<string> CheckForUpdateAsync(bool manual)
    {
        if (!UpdateService.CanSelfUpdate)
            return "Automatische Updates gibt es nur für die über GitHub installierte Version.";
        if (_updating) return "Update läuft bereits …";
        _updating = true;

        try
        {
            var info = await UpdateService.CheckAsync();
            if (info == null) return $"Mail ist aktuell (Version {UpdateService.Current.ToString(3)}).";

            _window?.Shell.ShowToast($"Update auf {info.Tag} wird heruntergeladen …", "download");
            var files = await UpdateService.DownloadAsync(info);

            // Don't throw away a mail that is being written – wait until the editor is closed.
            while (_window?.Shell.IsComposing == true)
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(15));

            _window?.Shell.ShowToast($"Update auf {info.Tag} wird installiert – Mail startet neu.", "refresh");
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(2));
            UpdateService.ApplyAndRestart(files);
            return $"Update auf {info.Tag} wird installiert …";
        }
        catch (Exception ex)
        {
            if (manual) return $"Update fehlgeschlagen: {ex.Message}";
            return "";
        }
        finally
        {
            _updating = false;
        }
    }

    private static bool IsAutostartLaunch()
    {
        if (Environment.GetCommandLineArgs().Any(a =>
                string.Equals(a, AutostartService.AutostartArgument, StringComparison.OrdinalIgnoreCase)))
            return true;

        try
        {
            return AppInstance.GetCurrent().GetActivatedEventArgs().Kind == ExtendedActivationKind.StartupTask;
        }
        catch
        {
            return false;
        }
    }

    public void ShowMainWindow()
    {
        if (_dispatcher == null || _window == null) return;
        _dispatcher.TryEnqueue(() => _window.ShowAndActivate());
    }

    public void HideToTray()
    {
        if (_window == null) return;
        _window.AppWindow.Hide();

        if (!SettingsService.Current.TrayHintShown)
        {
            SettingsService.Current.TrayHintShown = true;
            SettingsService.Save(notify: false);
            Tray.ShowBalloon("Mail läuft weiter",
                "Die App ist in den Infobereich minimiert und prüft weiterhin auf neue E-Mails.");
        }
    }

    public void ExitApplication()
    {
        if (_isExiting) return;
        _isExiting = true;

        _window?.SaveWindowState();
        _window?.Shell.Shutdown();
        Tray.Dispose();
        NotificationService.Shutdown();

        _window?.Close();
        Exit();
    }
}
