using MailClient.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Linq;

namespace MailClient.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly ObservableCollection<MailAccount> _accounts = [];

    public SettingsPage()
    {
        InitializeComponent();
        AccountListView.ItemsSource = _accounts;
        LoadAccounts();
        LoadTheme();
    }

    private void LoadAccounts()
    {
        _accounts.Clear();
        foreach (var acc in App.Accounts.LoadAccounts())
            _accounts.Add(acc);
    }

    private void LoadTheme()
    {
        var frame = App.MainWindowInstance.Content as FrameworkElement;
        var theme = frame?.RequestedTheme ?? ElementTheme.Default;
        ThemeRadio.SelectedIndex = theme switch
        {
            ElementTheme.Light => 1,
            ElementTheme.Dark => 2,
            _ => 0
        };
    }

    private async void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AccountDialog { XamlRoot = XamlRoot };
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            var accounts = App.Accounts.LoadAccounts();
            accounts.Add(dialog.Result);
            App.Accounts.SaveAccounts(accounts);
            App.Accounts.SavePassword(dialog.Result.Id, dialog.Password);
            LoadAccounts();
        }
    }

    private async void EditAccount_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not MailAccount account) return;

        var dialog = new AccountDialog(account) { XamlRoot = XamlRoot };
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            var accounts = App.Accounts.LoadAccounts();
            var idx = accounts.FindIndex(a => a.Id == account.Id);
            if (idx >= 0) accounts[idx] = dialog.Result;
            App.Accounts.SaveAccounts(accounts);
            if (!string.IsNullOrEmpty(dialog.Password))
                App.Accounts.SavePassword(dialog.Result.Id, dialog.Password);
            LoadAccounts();
        }
    }

    private async void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not MailAccount account) return;

        var confirm = new ContentDialog
        {
            Title = "Account entfernen",
            Content = $"'{account}' wirklich entfernen?",
            PrimaryButtonText = "Entfernen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            App.Accounts.DeleteAccount(account);
            LoadAccounts();
        }
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        var selected = (ThemeRadio.SelectedItem as RadioButton)?.Tag?.ToString();
        var theme = selected switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        if (App.MainWindowInstance.Content is FrameworkElement root)
            root.RequestedTheme = theme;
    }
}
