using MailClient.App.Helpers;
using MailClient.App.Services;
using MailClient.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace MailClient.App.Views;

public sealed partial class SettingsView : UserControl
{
    /// <summary>(account to select, reconnect current account)</summary>
    public event Action<Guid?, bool>? AccountsChanged;
    public event Action? CloseRequested;

    private readonly ObservableCollection<MailAccount> _accounts = [];
    private bool _loading;

    public SettingsView()
    {
        InitializeComponent();
        AccountsRepeater.ItemsSource = _accounts;
    }

    public async void Refresh()
    {
        _loading = true;

        _accounts.Clear();
        foreach (var acc in App.Accounts.LoadAccounts())
            _accounts.Add(acc);

        var s = SettingsService.Current;
        ReduceMotionToggle.IsOn = s.ReduceMotion;
        StartMinimizedToggle.IsOn = s.StartMinimized;
        CloseToTrayToggle.IsOn = s.CloseToTray;
        MinimizeToTrayToggle.IsOn = s.MinimizeToTray;
        NotificationsToggle.IsOn = s.Notifications;

        var syncItem = SyncCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == s.SyncIntervalMinutes.ToString());
        SyncCombo.SelectedItem = syncItem ?? SyncCombo.Items[3];

        DataDirText.Text = App.DataDir;
        SettingsInfoBar.IsOpen = false;

        AutostartToggle.IsOn = await AutostartService.IsEnabledAsync();
        _loading = false;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    // ── Accounts ─────────────────────────────────────────────────────────

    private async void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        var account = await AccountDialog.AddAsync(XamlRoot);
        if (account == null) return;
        Refresh();
        AccountsChanged?.Invoke(account.Id, true);
    }

    private async void EditAccount_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not MailAccount account) return;
        if (!await AccountDialog.EditAsync(XamlRoot, account)) return;
        Refresh();
        AccountsChanged?.Invoke(account.Id, true);
    }

    private async void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not MailAccount account) return;

        var confirm = new ContentDialog
        {
            Title = "Konto entfernen?",
            Content = $"„{account.Name}“ ({account.EmailAddress}) wird aus Mail entfernt. " +
                      "Die E-Mails auf dem Server bleiben erhalten.",
            PrimaryButtonText = "Entfernen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        App.Accounts.DeleteAccount(account);
        Refresh();
        AccountsChanged?.Invoke(null, false);
    }

    // ── Preferences ──────────────────────────────────────────────────────

    private void Sync_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SyncCombo.SelectedItem is not ComboBoxItem item) return;
        SettingsService.Current.SyncIntervalMinutes = int.Parse((string)item.Tag);
        SettingsService.Save();
    }

    private void Behavior_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = SettingsService.Current;
        s.StartMinimized = StartMinimizedToggle.IsOn;
        s.CloseToTray = CloseToTrayToggle.IsOn;
        s.MinimizeToTray = MinimizeToTrayToggle.IsOn;
        s.Notifications = NotificationsToggle.IsOn;
        s.ReduceMotion = ReduceMotionToggle.IsOn;
        SettingsService.Save();
    }

    private async void Autostart_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var enable = AutostartToggle.IsOn;
        var error = await AutostartService.SetEnabledAsync(enable);

        if (error != null)
        {
            _loading = true;
            AutostartToggle.IsOn = await AutostartService.IsEnabledAsync();
            _loading = false;

            SettingsInfoBar.Severity = InfoBarSeverity.Warning;
            SettingsInfoBar.Title = "Autostart";
            SettingsInfoBar.Message = error;
            SettingsInfoBar.IsOpen = true;
        }

        SettingsService.Current.StartWithWindows = AutostartToggle.IsOn;
        SettingsService.Save();
    }
}
