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

        if (IsAutostartLaunch() && SettingsService.Current.StartMinimized)
        {
            // Stay in the tray; the window is created but never shown.
            return;
        }

        _window.ShowAndActivate();
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
