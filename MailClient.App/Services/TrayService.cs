using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;

namespace MailClient.App.Services;

/// <summary>Notification-area (tray) icon with a context menu.</summary>
public sealed class TrayService : IDisposable
{
    private TaskbarIcon? _icon;

    public event Action? OpenRequested;
    public event Action? ComposeRequested;
    public event Action? SyncRequested;
    public event Action? ExitRequested;

    public void Create()
    {
        if (_icon != null) return;

        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Mail öffnen",
            Command = new RelayCommand(() => OpenRequested?.Invoke())
        });
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Neue E-Mail",
            Command = new RelayCommand(() => ComposeRequested?.Invoke())
        });
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Jetzt synchronisieren",
            Command = new RelayCommand(() => SyncRequested?.Invoke())
        });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Beenden",
            Command = new RelayCommand(() => ExitRequested?.Invoke())
        });

        _icon = new TaskbarIcon
        {
            ToolTipText = "Mail",
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(() => OpenRequested?.Invoke())
        };

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
            _icon.Icon = new System.Drawing.Icon(iconPath);
        else
            _icon.IconSource = new GeneratedIconSource { Text = "✉" };

        _icon.ForceCreate(false);
    }

    public void SetUnreadCount(int unread)
    {
        if (_icon == null) return;
        _icon.ToolTipText = unread > 0
            ? $"Mail – {unread} ungelesen"
            : "Mail";
    }

    public void ShowBalloon(string title, string message)
    {
        try { _icon?.ShowNotification(title, message); }
        catch { /* not critical */ }
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}
