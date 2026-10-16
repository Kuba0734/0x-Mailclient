using MailClient.App.Helpers;
using MailClient.Core.Models;
using MailClient.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MailClient.App.Views;

public sealed partial class MainPage : Page
{
    private readonly ObservableCollection<MailMessage> _messages = [];
    private List<MailMessage> _allMessages = [];
    private List<MailAccount> _accounts = [];
    private MailAccount? _currentAccount;
    private string _currentFolder = "INBOX";
    private ImapService? _imap;
    private CancellationTokenSource _cts = new();
    private MailMessage? _currentMessage;

    public MainPage()
    {
        InitializeComponent();
        MailListView.ItemsSource = _messages;
        LoadAccounts();
    }

    // ── Accounts ─────────────────────────────────────────────────────────

    private void LoadAccounts()
    {
        _accounts = App.Accounts.LoadAccounts();

        AccountCombo.Items.Clear();
        foreach (var acc in _accounts)
            AccountCombo.Items.Add(acc.ToString());

        if (_accounts.Count > 0)
        {
            AccountCombo.SelectedIndex = 0;
            NoAccountPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            NoAccountPanel.Visibility = Visibility.Visible;
        }
    }

    private async void AccountCombo_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        var idx = AccountCombo.SelectedIndex;
        if (idx < 0 || idx >= _accounts.Count) return;

        _currentAccount = _accounts[idx];
        await ConnectAndLoadAsync();
    }

    private async Task ConnectAndLoadAsync()
    {
        if (_currentAccount == null) return;

        var password = App.Accounts.LoadPassword(_currentAccount.Id);
        if (password == null)
        {
            ShowError("Passwort nicht gefunden. Bitte Account in den Einstellungen neu konfigurieren.");
            return;
        }

        _cts.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        // Dispose old connection — yield first so MailKit can process cancellation
        var oldImap = _imap;
        _imap = new ImapService();
        await Task.Yield();
        oldImap?.Dispose();

        ShowLoading(true);
        HideError();

        try
        {
            await _imap.ConnectAsync(
                _currentAccount.ImapHost,
                _currentAccount.ImapPort,
                _currentAccount.ImapUseSsl,
                _currentAccount.Username,
                password, ct);

            await LoadFolderAsync(_currentFolder, ct);
            await UpdateInboxBadgeAsync(ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowError($"Verbindung fehlgeschlagen: {ex.Message}");
        }
        finally
        {
            ShowLoading(false);
        }
    }

    private async Task LoadFolderAsync(string folderTag, CancellationToken ct)
    {
        if (_imap == null || !_imap.IsConnected) return;

        ShowLoading(true);
        ClearDetail();

        try
        {
            _allMessages = await _imap.GetMessagesAsync(folderTag, 80, ct);
            ApplySearch(null);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowError($"Ordner konnte nicht geladen werden: {ex.Message}");
        }
        finally
        {
            ShowLoading(false);
        }
    }

    private async Task UpdateInboxBadgeAsync(CancellationToken ct)
    {
        if (_imap == null) return;
        try
        {
            var unread = await _imap.GetUnreadCountAsync("INBOX", ct);
            InboxBadge.Value = unread;
            InboxBadge.Visibility = unread > 0
                ? Visibility.Visible : Visibility.Collapsed;
        }
        catch { }
    }

    // ── Navigation ────────────────────────────────────────────────────────

    private async void NavView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            MailView.Visibility = Visibility.Collapsed;
            SettingsFrame.Visibility = Visibility.Visible;
            SettingsFrame.Navigate(typeof(SettingsPage));
            return;
        }

        MailView.Visibility = Visibility.Visible;
        SettingsFrame.Visibility = Visibility.Collapsed;

        // Refresh accounts if returning from settings
        var prevCount = _accounts.Count;
        LoadAccounts();
        if (_accounts.Count != prevCount && _accounts.Count > 0)
        {
            AccountCombo.SelectedIndex = 0;
            return;
        }

        var item = args.SelectedItem as NavigationViewItem;
        var tag = item?.Tag?.ToString() ?? "INBOX";
        _currentFolder = tag;

        if (_currentAccount != null && _imap?.IsConnected == true)
            await LoadFolderAsync(tag, _cts.Token);
    }

    // ── Mail List ─────────────────────────────────────────────────────────

    private async void MailList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (MailListView.SelectedItem is not MailMessage selected) return;
        await ShowMessageDetailAsync(selected);
    }

    private async Task ShowMessageDetailAsync(MailMessage summary)
    {
        if (_imap == null || _currentAccount == null) return;

        MailDetailPanel.Visibility = Visibility.Visible;
        NoMailSelected.Visibility = Visibility.Collapsed;

        // Set header from summary first
        DetailSubject.Text = summary.Subject;
        DetailFrom.Text = summary.FromDisplay.Length > 0
            ? $"{summary.FromDisplay} <{summary.From}>"
            : summary.From;
        DetailTo.Text = $"An: {summary.To}";
        DetailDate.Text = summary.Date.ToString("dd. MMMM yyyy, HH:mm");

        // Load full body
        BodyLoadingRing.IsActive = true;
        MailWebView.Visibility = Visibility.Collapsed;

        try
        {
            _cts.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            // Reconnect if needed
            if (!_imap.IsConnected)
            {
                var pw = App.Accounts.LoadPassword(_currentAccount.Id);
                if (pw != null)
                    await _imap.ConnectAsync(
                        _currentAccount.ImapHost, _currentAccount.ImapPort,
                        _currentAccount.ImapUseSsl,
                        _currentAccount.Username, pw, ct);
            }

            var full = await _imap.GetMessageBodyAsync(_currentFolder, summary.Uid, ct);
            _currentMessage = full;

            // Mark as read in list
            summary.IsRead = true;

            // Display HTML
            await MailWebView.EnsureCoreWebView2Async();

            // Match WebView2 background to current app theme
            var isDark = (App.MainWindowInstance.Content as FrameworkElement)
                ?.ActualTheme == Microsoft.UI.Xaml.ElementTheme.Dark;
            MailWebView.DefaultBackgroundColor = isDark
                ? Windows.UI.Color.FromArgb(255, 28, 28, 28)
                : Windows.UI.Color.FromArgb(255, 255, 255, 255);

            var html = full.HtmlBody.Length > 0
                ? WrapHtml(full.HtmlBody, isDark)
                : WrapPlainText(full.TextBody, isDark);

            MailWebView.NavigateToString(html);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await MailWebView.EnsureCoreWebView2Async();
            MailWebView.NavigateToString(
                $"<p style='color:red;font-family:Segoe UI;padding:16px'>Fehler: {ex.Message}</p>");
            MailWebView.Visibility = Visibility.Visible;
        }
        finally
        {
            BodyLoadingRing.IsActive = false;
        }
    }

    private static string WrapHtml(string body, bool isDark)
    {
        var darkCss = isDark ? """
            @media (prefers-color-scheme: dark) {
              body { background:#1c1c1c !important; color:#e0e0e0 !important; }
              a { color:#6bb3f0 !important; }
            }
            """ : "";

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8">
              <meta name="color-scheme" content="light dark">
              <style>
                body { font-family:'Segoe UI',Arial,sans-serif; font-size:14px;
                       margin:0; padding:20px; max-width:960px; }
                img { max-width:100%; height:auto; }
                a { color:#2E75B6; }
                {{darkCss}}
              </style>
            </head>
            <body>{{body}}</body>
            </html>
            """;
    }

    private static string WrapPlainText(string text, bool isDark)
    {
        var encoded = System.Net.WebUtility.HtmlEncode(text);

        encoded = System.Text.RegularExpressions.Regex.Replace(
            encoded,
            @"(https?://[^\s&lt;&gt;&quot;]+)",
            "<a href=\"$1\" target=\"_blank\">$1</a>");

        encoded = System.Text.RegularExpressions.Regex.Replace(
            encoded,
            @"(?m)^(&gt;[^\n]*)",
            "<span class=\"quote\">$1</span>");

        var lines = encoded.Replace("\r\n", "\n").Replace("\r", "\n");

        var bg       = isDark ? "#1c1c1c" : "#ffffff";
        var fg       = isDark ? "#e0e0e0" : "#1b1b1b";
        var link     = isDark ? "#6bb3f0" : "#2E75B6";
        var quoteFg  = isDark ? "#909090" : "#6b6b6b";
        var quoteBdr = isDark ? "#505050" : "#c8c8c8";

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8">
              <style>
                body { font-family:'Segoe UI',Arial,sans-serif; font-size:14px;
                       line-height:1.6; margin:0; padding:20px;
                       background:{{bg}}; color:{{fg}};
                       max-width:900px; white-space:pre-wrap; word-wrap:break-word; }
                a { color:{{link}}; }
                .quote { color:{{quoteFg}}; border-left:3px solid {{quoteBdr}};
                         padding-left:8px; display:block; margin:2px 0; }
              </style>
            </head>
            <body>{{lines}}</body>
            </html>
            """;
    }

    private void MailWebView_NavigationCompleted(
        WebView2 sender,
        Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
    {
        MailWebView.Visibility = Visibility.Visible;
    }

    // ── Actions ───────────────────────────────────────────────────────────

    private async void Compose_Click(object sender, RoutedEventArgs e)
    {
        if (_currentAccount == null)
        {
            ShowError("Bitte zuerst einen Account auswählen.");
            return;
        }

        var dialog = new ComposeDialog(_currentAccount)
        {
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async void Reply_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMessage == null || _currentAccount == null) return;

        var dialog = new ComposeDialog(_currentAccount,
            to: _currentMessage.From,
            subject: $"Re: {_currentMessage.Subject}",
            body: BuildQuote(_currentMessage))
        {
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMessage == null || _currentAccount == null) return;

        var dialog = new ComposeDialog(_currentAccount,
            subject: $"Fwd: {_currentMessage.Subject}",
            body: BuildQuote(_currentMessage))
        {
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        // For now just remove from local list; full IMAP delete would move to Trash
        if (MailListView.SelectedItem is MailMessage msg)
        {
            _allMessages.Remove(msg);
            ApplySearch(null);
            ClearDetail();
        }
        await Task.CompletedTask;
    }

    private static string BuildQuote(MailMessage msg)
    {
        var date = msg.Date.ToString("dd. MMMM yyyy, HH:mm");
        return $"\n\n--- Am {date} schrieb {msg.From} ---\n{msg.TextBody}";
    }

    // ── Search ────────────────────────────────────────────────────────────

    private void Search_TextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        ApplySearch(sender.Text.Trim());
    }

    private void ApplySearch(string? query)
    {
        _messages.Clear();

        var filtered = string.IsNullOrEmpty(query)
            ? _allMessages
            : _allMessages.Where(m =>
                m.Subject.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                m.FromDisplay.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                m.From.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var m in filtered)
            _messages.Add(m);

        MailListView.Visibility = _messages.Count > 0 || _allMessages.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
        EmptyPanel.Visibility = _allMessages.Count == 0 && _accounts.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── UI helpers ────────────────────────────────────────────────────────

    private void ShowLoading(bool show)
    {
        LoadingPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) MailListView.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string msg)
    {
        MailInfoBar.Message = msg;
        MailInfoBar.Severity = InfoBarSeverity.Error;
        MailInfoBar.IsOpen = true;
    }

    private void HideError() => MailInfoBar.IsOpen = false;

    private void ClearDetail()
    {
        MailDetailPanel.Visibility = Visibility.Collapsed;
        NoMailSelected.Visibility = Visibility.Visible;
        _currentMessage = null;
    }
}
