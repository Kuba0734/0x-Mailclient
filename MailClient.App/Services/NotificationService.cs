using MailClient.Core.Models;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;
using System.Collections.Generic;
using System.IO;

namespace MailClient.App.Services;

/// <summary>Windows toast notifications for new mail.</summary>
public static class NotificationService
{
    private static bool _registered;

    public static void Initialize()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, _) =>
                App.Instance?.ShowMainWindow();

            if (AutostartService.IsPackaged)
            {
                AppNotificationManager.Default.Register();
            }
            else
            {
                var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppLogo.png");
                AppNotificationManager.Default.Register("Mail", new Uri(icon));
            }
            _registered = true;
        }
        catch
        {
            _registered = false;
        }
    }

    public static void ShowNewMail(string accountName, IReadOnlyList<MailMessage> messages)
    {
        if (!_registered || messages.Count == 0 || !SettingsService.Current.Notifications)
            return;

        try
        {
            var builder = new AppNotificationBuilder()
                .AddArgument("action", "open");

            if (messages.Count == 1)
            {
                var m = messages[0];
                builder.AddText(m.FromDisplay)
                       .AddText(m.Subject)
                       .AddText(m.Preview);
            }
            else
            {
                builder.AddText($"{messages.Count} neue E-Mails")
                       .AddText($"{messages[0].FromDisplay}: {messages[0].Subject}")
                       .AddText(accountName);
            }

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch { /* notifications are best effort */ }
    }

    public static void Shutdown()
    {
        if (!_registered) return;
        try { AppNotificationManager.Default.Unregister(); } catch { }
    }
}
