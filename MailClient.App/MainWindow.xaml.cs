using MailClient.App.Helpers;
using MailClient.App.Services;
using MailClient.App.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace MailClient.App;

public sealed partial class MainWindow : Window
{
    public MainPage Shell => ShellPage;

    private OverlappedPresenter Presenter => (OverlappedPresenter)AppWindow.Presenter;

    /// <summary>True while this window has keyboard focus.</summary>
    public bool IsActiveWindow { get; private set; }

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.Title = "Mail";

        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);

        Presenter.PreferredMinimumWidth = 900;
        Presenter.PreferredMinimumHeight = 560;

        RestoreWindowState();

        Activated += (_, e) => IsActiveWindow = e.WindowActivationState != WindowActivationState.Deactivated;
        AppWindow.Closing += AppWindow_Closing;
        AppWindow.Changed += AppWindow_Changed;

        ShellPage.SyncStateChanged += isSyncing => AnimationHelper.SetSpinning(SyncIcon, isSyncing);
        ShellPage.SearchFocusRequested += () => SearchBox.Focus(FocusState.Keyboard);
        ShellPage.SearchCleared += () => SearchBox.Text = "";
    }

    // ── Show / hide ──────────────────────────────────────────────────────

    public void ShowAndActivate()
    {
        // While the window is being brought back, a "minimized" state must not
        // trigger minimize-to-tray (that would hide it again right away).
        _isRestoring = true;
        try
        {
            if (Presenter.State == OverlappedPresenterState.Minimized)
                Presenter.Restore(true);
            AppWindow.Show(true);
            if (Presenter.State == OverlappedPresenterState.Minimized)
                Presenter.Restore(true);
            Activate();
            SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        }
        finally
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _isRestoring = false);
        }
    }

    private bool _isRestoring;

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (App.Instance!.IsExiting) return;

        if (SettingsService.Current.CloseToTray)
        {
            args.Cancel = true;
            SaveWindowState();
            App.Instance.HideToTray();
            return;
        }

        args.Cancel = true;
        App.Instance.ExitApplication();
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_isRestoring || !SettingsService.Current.MinimizeToTray || !sender.IsVisible) return;

        if (sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                // Re-check: the window may have been restored in the meantime.
                if (_isRestoring || !AppWindow.IsVisible ||
                    Presenter.State != OverlappedPresenterState.Minimized)
                    return;
                App.Instance?.HideToTray();
            });
        }
    }

    // ── Window placement ─────────────────────────────────────────────────

    private void RestoreWindowState()
    {
        var s = SettingsService.Current;
        var width = Math.Max(900, s.WindowWidth);
        var height = Math.Max(560, s.WindowHeight);

        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        width = Math.Min(width, area.Width);
        height = Math.Min(height, area.Height);

        int x, y;
        if (s.WindowX != int.MinValue && IsOnAnyScreen(s.WindowX, s.WindowY))
        {
            x = s.WindowX;
            y = s.WindowY;
        }
        else
        {
            x = area.X + (area.Width - width) / 2;
            y = area.Y + (area.Height - height) / 2;
        }

        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        if (s.WindowMaximized) Presenter.Maximize();
    }

    private static bool IsOnAnyScreen(int x, int y)
    {
        var area = DisplayArea.GetFromPoint(new PointInt32(x + 40, y + 20), DisplayAreaFallback.None);
        return area != null;
    }

    public void SaveWindowState()
    {
        var s = SettingsService.Current;
        s.WindowMaximized = Presenter.State == OverlappedPresenterState.Maximized;
        if (Presenter.State == OverlappedPresenterState.Restored)
        {
            s.WindowX = AppWindow.Position.X;
            s.WindowY = AppWindow.Position.Y;
            s.WindowWidth = AppWindow.Size.Width;
            s.WindowHeight = AppWindow.Size.Height;
        }
        s.PaneCollapsed = ShellPage.IsPaneCollapsed;
        SettingsService.Save(notify: false);
    }

    // ── Title bar ────────────────────────────────────────────────────────

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args) =>
        ShellPage.TogglePane();

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            ShellPage.SetLocalSearch(sender.Text);
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        _ = ShellPage.ServerSearchAsync(args.QueryText);

    private void SyncButton_Click(object sender, RoutedEventArgs e) =>
        _ = ShellPage.SyncNowAsync();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
