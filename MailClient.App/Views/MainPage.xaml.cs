using MailClient.App.Helpers;
using MailClient.App.Services;
using MailClient.Core.Models;
using MailClient.Core.Services;
using MailKit;
using MailKit.Security;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using MimeKit;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using VirtualKey = Windows.System.VirtualKey;
using Launcher = Windows.System.Launcher;

namespace MailClient.App.Views;

public sealed partial class MainPage : Page
{
    private const int PageSize = 50;
    private const double SidebarExpandedWidth = 260;
    private const double SidebarCollapsedWidth = 76;

    private enum ListState { Loading, List, Empty, NoAccount }

    public event Action<bool>? SyncStateChanged;
    public event Action? SearchFocusRequested;
    public event Action? SearchCleared;

    private readonly ObservableCollection<FolderItem> _folders =
    [
        new(ImapService.Inbox, "Posteingang", "inbox"),
        new(ImapService.Sent, "Gesendet", "send"),
        new(ImapService.Drafts, "Entwürfe", "draft"),
        new(ImapService.Archive, "Archiv", "archive"),
        new(ImapService.Spam, "Spam", "spam"),
        new(ImapService.Trash, "Papierkorb", "trash"),
    ];

    private ObservableCollection<MailMessage> _messages = [];
    private List<MailMessage> _all = [];
    private List<MailAccount> _accounts = [];
    private MailAccount? _account;
    private ImapService? _imap;

    private string _folder = ImapService.Inbox;
    private int _total;
    private int _loadVersion, _readVersion, _connectVersion;
    private MailMessage? _selected;
    private MailMessage? _full;
    private string _localQuery = "";
    private string? _serverQuery;
    private bool _syncing, _loadingMore, _composeOpen, _initialized, _suppressSelection;
    private uint _lastInboxUid;

    private readonly DispatcherQueueTimer _syncTimer;
    private readonly DispatcherQueueTimer _searchDebounce;
    private readonly DispatcherQueueTimer _toastTimer;

    private bool _webViewReady;
    private string? _tempHtmlPath;

    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "MailClient");

    private static readonly HashSet<string> BlockedOpenExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
        ".msi", ".msp", ".scr", ".pif", ".lnk", ".hta", ".cpl", ".reg", ".jar", ".appx", ".msix"
    };

    public bool IsPaneCollapsed { get; private set; }

    public MainPage()
    {
        InitializeComponent();

        FolderList.ItemsSource = _folders;
        MailList.ItemsSource = _messages;
        SkeletonList.ItemsSource = Enumerable.Range(0, 9).ToList();
        Motion.SetOpacity(ComposeSmoke, 0);

        _syncTimer = DispatcherQueue.CreateTimer();
        _syncTimer.Tick += (_, _) => _ = SyncNowAsync(manual: false);

        _searchDebounce = DispatcherQueue.CreateTimer();
        _searchDebounce.Interval = TimeSpan.FromMilliseconds(250);
        _searchDebounce.IsRepeating = false;
        _searchDebounce.Tick += (_, _) => ApplyFilter();

        _toastTimer = DispatcherQueue.CreateTimer();
        _toastTimer.IsRepeating = false;
        _toastTimer.Tick += (_, _) => _ = HideToastAsync();

        Composer.CloseRequested += (_, _) => _ = CloseComposeAsync();
        Composer.Sent += Composer_Sent;
        Composer.DraftSaveRequested += Composer_DraftSaveRequested;

        SettingsPanel.AccountsChanged += (selectId, reconnect) => LoadAccounts(selectId, reconnect);
        SettingsPanel.CloseRequested += () => _ = ShowSettingsAsync(false);
        SettingsService.Changed += ApplySyncInterval;
        SettingsService.Changed += ApplyMotionSettings;

        Loaded += OnLoaded;
    }

    /// <summary>Theme transitions are switched off together with the app's own motion.</summary>
    private void ApplyMotionSettings()
    {
        MailList.ItemContainerTransitions = Motion.Enabled
            ? new TransitionCollection { new RepositionThemeTransition() }
            : null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        foreach (var el in new UIElement[]
                 { EmptyPanel, NoAccountPanel, ReaderEmpty, ReaderContent, AttachmentsScroller, SkeletonHost, LoadMorePanel })
            Motion.ImplicitFade(el);

        Composer.SetShadowReceiver(ComposeSmoke);
        ApplyMotionSettings();

        if (SettingsService.Current.PaneCollapsed)
            SetPaneCollapsed(true, animate: false);

        Motion.FadeIn(Sidebar, 0, fromX: -12, duration: "MotionSlow");
        Motion.FadeIn(ContentLayer, 12, delayMs: 60, duration: "MotionSlow");

        FolderList.SelectedIndex = 0;
        ApplySyncInterval();
        LoadAccounts(SettingsService.Current.LastAccountId, reconnect: false);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Accounts
    // ══════════════════════════════════════════════════════════════════

    private void LoadAccounts(Guid? selectId, bool reconnect)
    {
        _accounts = App.Accounts.LoadAccounts();

        if (_accounts.Count == 0)
        {
            _account = null;
            ++_connectVersion;
            ++_loadVersion;
            DisposeImap();
            _all = [];
            SetMessages([]);
            ClearReader();
            foreach (var f in _folders) f.Count = 0;
            App.Instance?.Tray.SetUnreadCount(0);
            UpdateAccountButton();
            RebuildAccountMenu();
            UpdateFolderHeader();
            ShowState(ListState.NoAccount);
            return;
        }

        var target = _accounts.FirstOrDefault(a => a.Id == selectId)
                     ?? _accounts.FirstOrDefault(a => a.Id == _account?.Id)
                     ?? _accounts[0];

        if (reconnect || target.Id != _account?.Id || _imap == null)
            _ = SwitchAccountAsync(target);
        else
        {
            _account = target;
            UpdateAccountButton();
            RebuildAccountMenu();
        }
    }

    private async Task SwitchAccountAsync(MailAccount account)
    {
        _account = account;
        SettingsService.Current.LastAccountId = account.Id;
        SettingsService.Save(notify: false);

        UpdateAccountButton();
        RebuildAccountMenu();

        var cv = ++_connectVersion;
        ++_loadVersion;
        DisposeImap();

        _all = [];
        _serverQuery = null;
        _localQuery = "";
        SearchCleared?.Invoke();
        SetMessages([]);
        ClearReader();
        HideError();
        _lastInboxUid = 0;
        foreach (var f in _folders)
        {
            f.Count = 0;
            f.IsAvailable = true;
        }
        UpdateFolderHeader();

        var password = App.Accounts.LoadPassword(account.Id);
        if (password == null)
        {
            ShowError("Für dieses Konto ist kein Passwort gespeichert. Bitte das Konto in den Einstellungen bearbeiten.");
            ShowEmpty("Nicht angemeldet", "Passwort fehlt.", "lock");
            return;
        }

        var imap = new ImapService();
        _imap = imap;
        ShowState(ListState.Loading);

        try
        {
            await imap.ConnectAsync(account.ImapHost, account.ImapPort, account.ImapUseSsl,
                account.Username, password);
            if (cv != _connectVersion) return;

            foreach (var f in _folders)
                f.IsAvailable = imap.HasFolder(f.Tag);

            if (!imap.HasFolder(_folder))
                _folder = ImapService.Inbox;
            SelectFolderItem(_folder);

            await LoadFolderAsync(_folder);
            if (cv != _connectVersion) return;

            await RefreshCountsAsync();
            var (_, maxUid) = await imap.GetNewInboxMessagesAsync(0);
            if (cv == _connectVersion) _lastInboxUid = maxUid;
        }
        catch (Exception ex) when (cv == _connectVersion)
        {
            ShowError($"Verbindung fehlgeschlagen: {Friendly(ex)}");
            ShowEmpty("Keine Verbindung", "Prüfe deine Internetverbindung und die Kontoeinstellungen.", "offline");
        }
        catch
        {
            // superseded by another account switch
        }
    }

    private void DisposeImap()
    {
        var old = _imap;
        _imap = null;
        if (old != null) _ = Task.Run(old.Dispose);
    }

    private void UpdateAccountButton()
    {
        if (_account == null)
        {
            AccountInitials.Text = "+";
            AccountNameText.Text = "Kein Konto";
            AccountMailText.Text = "Konto hinzufügen";
            return;
        }

        AccountInitials.Text = Fmt.Initials(_account.Name);
        AccountNameText.Text = _account.Name;
        AccountMailText.Text = _account.EmailAddress;
        Motion.FadeIn(AccountAvatar, 0, fromScale: (float)Tokens.Double("MotionModalScale"));
    }

    private void RebuildAccountMenu()
    {
        AccountMenu.Items.Clear();
        foreach (var acc in _accounts)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = acc.EmailAddress.Length > 0 && acc.Name != acc.EmailAddress
                    ? $"{acc.Name}  ·  {acc.EmailAddress}"
                    : acc.Name,
                GroupName = "Accounts",
                IsChecked = acc.Id == _account?.Id
            };
            item.Click += (_, _) =>
            {
                if (acc.Id != _account?.Id) _ = SwitchAccountAsync(acc);
            };
            AccountMenu.Items.Add(item);
        }

        if (_accounts.Count > 0)
            AccountMenu.Items.Add(new MenuFlyoutSeparator());

        var add = new MenuFlyoutItem { Text = "Konto hinzufügen", Icon = Icon("user-plus") };
        add.Click += (_, _) => _ = AddAccountAsync();
        AccountMenu.Items.Add(add);

        var manage = new MenuFlyoutItem { Text = "Konten verwalten", Icon = Icon("settings") };
        manage.Click += (_, _) => _ = ShowSettingsAsync(true);
        AccountMenu.Items.Add(manage);
    }

    private async Task AddAccountAsync()
    {
        var account = await AccountDialog.AddAsync(XamlRoot);
        if (account == null) return;
        LoadAccounts(account.Id, reconnect: true);
        SettingsPanel.Refresh();
        ShowToast("Konto hinzugefügt", "user-plus");
    }

    private void AddAccount_Click(object sender, RoutedEventArgs e) => _ = AddAccountAsync();

    // ══════════════════════════════════════════════════════════════════
    //  Folders & list
    // ══════════════════════════════════════════════════════════════════

    private async void FolderList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not FolderItem item) return;

        var settingsWasOpen = SettingsHost.Visibility == Visibility.Visible;
        if (settingsWasOpen) await ShowSettingsAsync(false);

        if (!item.IsAvailable)
        {
            ShowToast($"Der Ordner \"{item.Name}\" existiert auf diesem Server nicht.", isError: true);
            DispatcherQueue.TryEnqueue(() => SelectFolderItem(_folder));
            return;
        }

        if (item.Tag == _folder && _serverQuery == null && _localQuery.Length == 0 && settingsWasOpen)
            return;

        _folder = item.Tag;
        _localQuery = "";
        SearchCleared?.Invoke();
        Motion.FadeIn(FolderHeader, 0, fromX: -8);
        await LoadFolderAsync(item.Tag);
    }

    private void SelectFolderItem(string tag) =>
        FolderList.SelectedItem = _folders.FirstOrDefault(f => f.Tag == tag);

    private async Task LoadFolderAsync(string tag, bool quiet = false)
    {
        if (_imap == null) return;
        var imap = _imap;
        var v = ++_loadVersion;

        _serverQuery = null;
        UpdateFolderHeader();

        if (!quiet)
        {
            ShowState(ListState.Loading);
            ClearReader();
        }

        try
        {
            var (messages, total) = await imap.GetMessagesAsync(tag, 0, PageSize);
            if (v != _loadVersion) return;

            _all = messages;
            _total = total;
            HideError();
            ApplyFilter();
        }
        catch (MailFolderMissingException) when (v == _loadVersion)
        {
            _all = [];
            _total = 0;
            SetMessages([]);
            ShowEmpty("Ordner nicht verfügbar", "Dieser Ordner existiert auf dem Server nicht.", "folder");
        }
        catch (Exception ex) when (v == _loadVersion)
        {
            _all = [];
            _total = 0;
            SetMessages([]);
            ShowError($"Ordner konnte nicht geladen werden: {Friendly(ex)}");
            ShowEmpty("Laden fehlgeschlagen", "Versuche es mit F5 erneut.", "alert");
        }
    }

    private bool Matches(MailMessage m)
    {
        switch (FilterSegmented?.SelectedIndex ?? 0)
        {
            case 1 when m.IsRead:
            case 2 when !m.IsFlagged:
                return false;
        }

        if (_localQuery.Length == 0) return true;
        return m.Subject.Contains(_localQuery, StringComparison.OrdinalIgnoreCase)
            || m.FromDisplay.Contains(_localQuery, StringComparison.OrdinalIgnoreCase)
            || m.From.Contains(_localQuery, StringComparison.OrdinalIgnoreCase)
            || m.To.Contains(_localQuery, StringComparison.OrdinalIgnoreCase)
            || m.Preview.Contains(_localQuery, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyFilter()
    {
        if (_account == null)
        {
            ShowState(ListState.NoAccount);
            return;
        }

        var list = _all.Where(Matches).ToList();
        var keep = _selected;

        SetMessages(list);

        if (keep != null && list.Contains(keep))
        {
            _suppressSelection = true;
            MailList.SelectedItem = keep;
            _suppressSelection = false;
        }

        UpdateFolderHeader();
        UpdateLoadMore();

        if (list.Count > 0)
        {
            ShowState(ListState.List);
            return;
        }

        if (_serverQuery != null || _localQuery.Length > 0)
            ShowEmpty("Keine Treffer", "Für deine Suche wurden keine E-Mails gefunden.", "search");
        else if (FilterSegmented.SelectedIndex == 1)
            ShowEmpty("Alles gelesen", "Hier gibt es keine ungelesenen E-Mails.", "mail-open");
        else if (FilterSegmented.SelectedIndex == 2)
            ShowEmpty("Nichts markiert", "Markierte E-Mails erscheinen hier.", "flag");
        else
            ShowEmpty("Alles erledigt!", "Dieser Ordner ist leer.", "check");
    }

    private void SetMessages(List<MailMessage> list)
    {
        _staggerUntil = DateTime.Now + TimeSpan.FromMilliseconds(600);
        _animateIn.Clear();
        _messages = new ObservableCollection<MailMessage>(list);
        MailList.ItemsSource = _messages;
    }

    private void UpdateLoadMore() =>
        LoadMorePanel.Visibility = _serverQuery == null && _all.Count > 0 && _all.Count < _total
            ? Visibility.Visible : Visibility.Collapsed;

    private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        ApplyFilter();
    }

    private void UpdateFolderHeader()
    {
        var folderItem = _folders.FirstOrDefault(f => f.Tag == _folder);

        if (_serverQuery != null)
        {
            FolderTitle.Text = "Suchergebnisse";
            FolderSubtitle.Text = $"„{_serverQuery}“ in {folderItem?.Name} · {_all.Count} Treffer";
            return;
        }

        FolderTitle.Text = folderItem?.Name ?? "Posteingang";

        if (_account == null)
        {
            FolderSubtitle.Text = "";
            return;
        }

        var unread = _folder is ImapService.Inbox or ImapService.Spam
            ? folderItem?.Count ?? 0
            : _all.Count(m => !m.IsRead);

        var text = _total == 1 ? "1 E-Mail" : $"{_total} E-Mails";
        if (unread > 0) text += $" · {unread} ungelesen";
        FolderSubtitle.Text = _total > 0 || unread > 0 ? text : _account.EmailAddress;
    }

    private async void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        if (_imap == null || _loadingMore) return;
        _loadingMore = true;
        LoadMoreButton.Visibility = Visibility.Collapsed;
        LoadMoreRing.IsActive = true;
        var v = _loadVersion;

        try
        {
            var (more, total) = await _imap.GetMessagesAsync(_folder, _all.Count, PageSize);
            if (v != _loadVersion) return;
            _total = total;

            var known = _all.Select(m => m.Uid).ToHashSet();
            var added = more.Where(m => !known.Contains(m.Uid)).ToList();
            _all.AddRange(added);
            foreach (var m in added.Where(Matches))
            {
                _animateIn.Add(m);
                _messages.Add(m);
            }

            UpdateFolderHeader();
        }
        catch (Exception ex)
        {
            ShowError($"Weitere E-Mails konnten nicht geladen werden: {Friendly(ex)}");
        }
        finally
        {
            _loadingMore = false;
            LoadMoreButton.Visibility = Visibility.Visible;
            LoadMoreRing.IsActive = false;
            UpdateLoadMore();
        }
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => _ = LoadFolderAsync(_folder);

    private async void MarkAllRead_Click(object sender, RoutedEventArgs e)
    {
        if (_imap == null) return;
        try
        {
            await _imap.MarkAllReadAsync(_folder);
            foreach (var m in _all) m.IsRead = true;
            var item = _folders.FirstOrDefault(f => f.Tag == _folder);
            if (item != null && _folder != ImapService.Drafts) item.Count = 0;
            UpdateTrayCount();
            UpdateFolderHeader();
            if (_selected != null) UpdateReaderToolbar(_selected);
            ShowToast("Alle E-Mails als gelesen markiert", "mail-open");
        }
        catch (Exception ex)
        {
            ShowToast($"Fehlgeschlagen: {Friendly(ex)}", isError: true);
        }
    }

    // ── List state visuals ───────────────────────────────────────────────

    private void ShowState(ListState state)
    {
        SkeletonHost.Visibility = state == ListState.Loading ? Visibility.Visible : Visibility.Collapsed;
        MailList.Visibility = state == ListState.List ? Visibility.Visible : Visibility.Collapsed;
        EmptyPanel.Visibility = state == ListState.Empty ? Visibility.Visible : Visibility.Collapsed;
        NoAccountPanel.Visibility = state == ListState.NoAccount ? Visibility.Visible : Visibility.Collapsed;
        FilterSegmented.Visibility = state == ListState.NoAccount ? Visibility.Collapsed : Visibility.Visible;
        Motion.Shimmer(ShimmerBar, state == ListState.Loading);
    }

    private void SkeletonHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The shimmer sweeps via translation; clip it to the skeleton area.
        SkeletonHost.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        if (SkeletonHost.Visibility == Visibility.Visible) Motion.Shimmer(ShimmerBar, true);
    }

    // ── Staggered list entrance ──────────────────────────────────────────

    private DateTime _staggerUntil;
    private readonly HashSet<MailMessage> _animateIn = [];

    private void MailList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        var max = (int)Tokens.Double("MotionStaggerMax", 10);

        if (DateTime.Now < _staggerUntil && args.ItemIndex < max)
            Motion.FadeIn(args.ItemContainer, 10, args.ItemIndex * (int)Tokens.Double("MotionStaggerStep", 50));
        else if (args.Item is MailMessage m && _animateIn.Remove(m))
            Motion.FadeIn(args.ItemContainer, -8, fromScale: (float)Tokens.Double("MotionModalScale"));
        else
            Motion.Reset(args.ItemContainer);
    }

    private void ShowEmpty(string title, string subtitle, string icon)
    {
        EmptyTitle.Text = title;
        EmptySubtitle.Text = subtitle;
        Svg.SetKind(EmptyIcon, icon);
        ShowState(ListState.Empty);
    }

    private void ShowError(string message)
    {
        MailInfoBar.Title = "Fehler";
        MailInfoBar.Message = message;
        MailInfoBar.Severity = InfoBarSeverity.Error;
        MailInfoBar.IsOpen = true;
    }

    private void HideError() => MailInfoBar.IsOpen = false;

    // ── Hover quick actions ──────────────────────────────────────────────

    private void MailItem_PointerEntered(object sender, PointerRoutedEventArgs e) => SetHoverActions(sender, true);
    private void MailItem_PointerExited(object sender, PointerRoutedEventArgs e) => SetHoverActions(sender, false);

    private static void SetHoverActions(object sender, bool show)
    {
        if (sender is not Grid grid) return;
        var panel = grid.Children.OfType<StackPanel>().FirstOrDefault(c => c.Name == "HoverActions");
        if (panel == null) return;
        Motion.FadeTo(panel, show ? 1 : 0);
        panel.IsHitTestVisible = show;
    }

    private void HoverActions_Loaded(object sender, RoutedEventArgs e) =>
        Motion.SetOpacity((UIElement)sender, 0);

    private void HoverRead_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MailMessage m) _ = ToggleReadAsync(m);
    }

    private void HoverFlag_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MailMessage m) _ = ToggleFlagAsync(m);
    }

    private void HoverDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MailMessage m) _ = DeleteMessageAsync(m);
    }

    private void MailList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Delete && _selected != null)
        {
            e.Handled = true;
            _ = DeleteMessageAsync(_selected);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Reader
    // ══════════════════════════════════════════════════════════════════

    private async void MailList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (MailList.SelectedItem is MailMessage m && m != _selected)
            await ShowMessageAsync(m);
    }

    private async Task ShowMessageAsync(MailMessage summary)
    {
        if (_imap == null) return;
        var imap = _imap;
        var v = ++_readVersion;

        _selected = summary;
        _full = null;

        ReaderEmpty.Visibility = Visibility.Collapsed;
        ReaderContent.Visibility = Visibility.Visible;
        FillHeader(summary);
        UpdateReaderToolbar(summary);
        Motion.FadeIn(ReaderHeader, 8);
        Motion.FadeIn(BodyCard, 12, delayMs: (int)Tokens.Double("MotionStaggerStep"));

        AttachmentsScroller.Visibility = Visibility.Collapsed;
        Motion.FadeTo(MailWebView, 0, "MotionFast");
        BodyLoadingRing.IsActive = true;

        var wasUnread = !summary.IsRead;

        try
        {
            var full = await imap.GetMessageBodyAsync(summary.FolderTag, summary.Uid, markAsRead: true);
            if (v != _readVersion) return;

            full.IsFlagged = summary.IsFlagged;
            full.IsDraft = summary.IsDraft;
            _full = full;

            if (wasUnread)
            {
                summary.IsRead = true;
                AdjustUnread(summary.FolderTag, -1);
                UpdateReaderToolbar(summary);
            }

            FillHeader(full);

            if (full.Attachments.Count > 0)
            {
                AttachmentsRepeater.ItemsSource = full.Attachments;
                AttachmentsScroller.Visibility = Visibility.Visible;
            }

            await RenderBodyAsync(full, v);
        }
        catch (Exception ex) when (v == _readVersion)
        {
            BodyLoadingRing.IsActive = false;
            await ShowBodyErrorAsync(ex);
        }
        catch
        {
            // superseded
        }
    }

    private void FillHeader(MailMessage m)
    {
        DetailSubject.Text = m.Subject;
        DetailFromName.Text = m.FromDisplay;
        DetailFromAddress.Text = m.FromAddress.Length > 0 &&
            !string.Equals(m.FromAddress, m.FromDisplay, StringComparison.OrdinalIgnoreCase)
            ? m.FromAddress : "";
        DetailDate.Text = Fmt.LongDate(m.Date);
        DetailInitials.Text = Fmt.Initials(m.FromDisplay);

        // Collapse the recipient list again when a different message is shown.
        var key = $"{m.FolderTag}/{m.Uid}";
        if (key != _recipientsKey)
        {
            _recipientsKey = key;
            _recipientsExpanded = false;
        }
        _recipientsTo = ParseRecipients(m.To);
        _recipientsCc = ParseRecipients(m.Cc);
        UpdateRecipients();
    }

    // ── Recipients (collapsible) ─────────────────────────────────────────

    private const int CollapsedToCount = 3;
    private const int CollapsedCcCount = 2;

    private List<(string Name, string Address)> _recipientsTo = [];
    private List<(string Name, string Address)> _recipientsCc = [];
    private bool _recipientsExpanded;
    private string? _recipientsKey;

    private static List<(string Name, string Address)> ParseRecipients(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        if (!InternetAddressList.TryParse(text, out var list))
            return [(text, "")];

        return list.Mailboxes
            .Select(mb =>
            {
                var name = (mb.Name ?? "").Trim().Trim('"', '\'').Trim();
                if (name.Length == 0 || string.Equals(name, mb.Address, StringComparison.OrdinalIgnoreCase))
                    name = mb.Address;
                return (name, mb.Address);
            })
            .ToList();
    }

    private static string FormatRecipients(List<(string Name, string Address)> list, int? limit)
    {
        if (limit is int max && list.Count > max)
        {
            var shown = string.Join(", ", list.Take(max).Select(r => r.Name));
            var rest = list.Count - max;
            return $"{shown} und {rest} {(rest == 1 ? "weiterer" : "weitere")}";
        }

        return limit != null
            ? string.Join(", ", list.Select(r => r.Name))
            : string.Join(", ", list.Select(r =>
                r.Address.Length == 0 || r.Name == r.Address ? r.Name : $"{r.Name} <{r.Address}>"));
    }

    private void UpdateRecipients()
    {
        var total = _recipientsTo.Count + _recipientsCc.Count;
        var collapsible = _recipientsTo.Count > CollapsedToCount || _recipientsCc.Count > CollapsedCcCount;

        int? toLimit = _recipientsExpanded ? null : CollapsedToCount;
        int? ccLimit = _recipientsExpanded ? null : CollapsedCcCount;

        DetailTo.Text = _recipientsTo.Count > 0 ? $"An: {FormatRecipients(_recipientsTo, toLimit)}" : "";
        DetailTo.Visibility = _recipientsTo.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailCc.Text = _recipientsCc.Count > 0 ? $"CC: {FormatRecipients(_recipientsCc, ccLimit)}" : "";
        DetailCc.Visibility = _recipientsCc.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        RecipientsToggle.Visibility = collapsible ? Visibility.Visible : Visibility.Collapsed;
        RecipientsToggleText.Text = _recipientsExpanded ? "Weniger anzeigen" : $"Alle {total} Empfänger anzeigen";
        Svg.SetKind(RecipientsToggleIcon, _recipientsExpanded ? "chevron-up" : "chevron-down");

        // Expanded long lists scroll instead of pushing the message body out of view.
        RecipientsScroller.MaxHeight = _recipientsExpanded ? 180 : double.PositiveInfinity;
        RecipientsScroller.ChangeView(null, 0, null, true);
    }

    private void RecipientsToggle_Click(object sender, RoutedEventArgs e)
    {
        _recipientsExpanded = !_recipientsExpanded;
        UpdateRecipients();
        Motion.FadeIn(RecipientsScroller, _recipientsExpanded ? -6 : 6);
    }

    private void UpdateReaderToolbar(MailMessage m)
    {
        Svg.SetKind(ToggleReadIcon, Fmt.ReadIcon(m.IsRead));
        ToolTipService.SetToolTip(ToggleReadButton, Fmt.ReadLabel(m.IsRead) + " (Strg+Q)");

        FlagDot.Visibility = Fmt.Vis(m.IsFlagged);
        ToolTipService.SetToolTip(FlagButton, Fmt.FlagLabel(m.IsFlagged));

        var inArchive = _folder == ImapService.Archive;
        ArchiveButton.Visibility = _imap?.HasFolder(ImapService.Archive) == true && !inArchive
            ? Visibility.Visible : Visibility.Collapsed;

        var inSpam = _folder == ImapService.Spam;
        SpamButton.Visibility = _imap?.HasFolder(ImapService.Spam) == true
            ? Visibility.Visible : Visibility.Collapsed;
        Svg.SetKind(SpamIcon, inSpam ? "shield-check" : "spam");
        ToolTipService.SetToolTip(SpamButton, inSpam ? "Kein Spam (in den Posteingang)" : "Als Spam markieren");

        EditDraftButton.Visibility = _folder == ImapService.Drafts ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearReader()
    {
        ++_readVersion;
        _selected = null;
        _full = null;
        ReaderContent.Visibility = Visibility.Collapsed;
        ReaderEmpty.Visibility = Visibility.Visible;
        BodyLoadingRing.IsActive = false;
        AttachmentsRepeater.ItemsSource = null;
    }

    // ── Body rendering (WebView2) ────────────────────────────────────────

    private async Task EnsureWebViewAsync()
    {
        if (_webViewReady) return;

        await MailWebView.EnsureCoreWebView2Async();
        var core = MailWebView.CoreWebView2;
        core.Settings.IsScriptEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsZoomControlEnabled = true;

        core.NavigationStarting += (_, args) =>
        {
            if (IsInternalUri(args.Uri)) return;
            args.Cancel = true;
            OpenExternal(args.Uri);
        };
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            OpenExternal(args.Uri);
        };

        MailWebView.NavigationCompleted += (_, _) =>
        {
            BodyLoadingRing.IsActive = false;
            Motion.FadeTo(MailWebView, 1, "MotionSlow");
        };

        MailWebView.DefaultBackgroundColor = Colors.Transparent;
        _webViewReady = true;
    }

    private bool IsInternalUri(string uri) =>
        uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
        uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
        (_tempHtmlPath != null && uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(new Uri(uri).LocalPath, _tempHtmlPath, StringComparison.OrdinalIgnoreCase));

    private void OpenExternal(string uri)
    {
        if (uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            var rest = uri[7..];
            var parts = rest.Split('?', 2);
            var req = new ComposeRequest { To = Uri.UnescapeDataString(parts[0]) };
            if (parts.Length > 1)
            {
                foreach (var kv in parts[1].Split('&'))
                {
                    var p = kv.Split('=', 2);
                    if (p.Length == 2 && p[0].Equals("subject", StringComparison.OrdinalIgnoreCase))
                        req.Subject = Uri.UnescapeDataString(p[1]);
                    if (p.Length == 2 && p[0].Equals("cc", StringComparison.OrdinalIgnoreCase))
                        req.Cc = Uri.UnescapeDataString(p[1]);
                }
            }
            OpenCompose(req);
            return;
        }

        if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https"))
            _ = Launcher.LaunchUriAsync(u);
    }

    private async Task RenderBodyAsync(MailMessage full, int version)
    {
        await EnsureWebViewAsync();
        if (version != _readVersion) return;

        var html = full.HtmlBody.Length > 0
            ? WrapHtml(full.HtmlBody)
            : WrapPlainText(full.PlainBody);

        NavigateHtml(html);
    }

    private void NavigateHtml(string html)
    {
        // NavigateToString is limited to ~2 MB; larger mails (e.g. inline images) go through a temp file.
        if (html.Length > 1_500_000)
        {
            Directory.CreateDirectory(TempDir);
            _tempHtmlPath = Path.Combine(TempDir, "view.html");
            File.WriteAllText(_tempHtmlPath, html);
            MailWebView.CoreWebView2.Navigate(new Uri(_tempHtmlPath).AbsoluteUri);
        }
        else
        {
            MailWebView.NavigateToString(html);
        }
    }

    private async Task ShowBodyErrorAsync(Exception ex)
    {
        await EnsureWebViewAsync();
        var fg = Tokens.Css("DangerColor");
        NavigateHtml($"""
            <html><body style="font-family:Inter,'Segoe UI';padding:24px;color:{fg};background:transparent">
            <b>Die Nachricht konnte nicht geladen werden.</b><br>{System.Net.WebUtility.HtmlEncode(Friendly(ex))}
            </body></html>
            """);
    }

    private static string WrapHtml(string body) => $$"""
        <!DOCTYPE html>
        <html>
        <head>
          <meta charset="utf-8">
          <meta name="color-scheme" content="light">
          <style>
            html { background:{{Tokens.Css("PaperColor")}}; }
            body { font-family:Inter,'Segoe UI Variable Text','Segoe UI',Arial,sans-serif; font-size:14px;
                   line-height:1.5; color:{{Tokens.Css("PaperTextColor")}}; margin:0; padding:24px; overflow-wrap:anywhere; }
            img { max-width:100%; height:auto; }
            a { color:{{Tokens.Css("PaperTextColor")}}; }
            blockquote { border-left:3px solid {{Tokens.Css("PaperBorderColor")}}; margin:8px 0; padding-left:12px; color:{{Tokens.Css("PaperMutedColor")}}; }
            ::-webkit-scrollbar { width:10px; height:10px; }
            ::-webkit-scrollbar-thumb { background:{{Tokens.Css("PaperBorderColor")}}; border-radius:6px; border:2px solid {{Tokens.Css("PaperColor")}}; }
          </style>
        </head>
        <body>{{body}}</body>
        </html>
        """;

    private static string WrapPlainText(string text)
    {
        var encoded = System.Net.WebUtility.HtmlEncode(text).Replace("\r\n", "\n").Replace("\r", "\n");

        encoded = Regex.Replace(encoded,
            @"https?://[^\s<>""]+?(?=(&lt;|&gt;|&quot;|[\s)\]]|$))",
            m => $"<a href=\"{m.Value}\">{m.Value}</a>");

        encoded = Regex.Replace(encoded, @"(?m)^(&gt;.*)$", "<span class=\"quote\">$1</span>");

        var fg = Tokens.Css("TextPrimaryColor");
        var link = Tokens.Css("AccentColor");
        var quoteFg = Tokens.Css("TextSecondaryColor");
        var quoteBorder = Tokens.Css("BorderStrongColor");
        var scroll = Tokens.Css("BorderStrongColor");

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8">
              <style>
                html, body { background:transparent; }
                body { font-family:Inter,'Segoe UI Variable Text','Segoe UI',Arial,sans-serif; font-size:14px;
                       line-height:1.6; margin:0; padding:24px; color:{{fg}};
                       white-space:pre-wrap; overflow-wrap:anywhere; }
                a { color:{{link}}; }
                .quote { color:{{quoteFg}}; border-left:3px solid {{quoteBorder}}; padding-left:10px; display:inline-block; }
                ::-webkit-scrollbar { width:10px; }
                ::-webkit-scrollbar-thumb { background:{{scroll}}; border-radius:6px; }
              </style>
            </head>
            <body>{{encoded}}</body>
            </html>
            """;
    }

    // ── Attachments ──────────────────────────────────────────────────────

    private void Attachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not MailAttachment att) return;

        var menu = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = "Öffnen", Icon = Icon("external") };
        open.Click += (_, _) => _ = OpenAttachmentAsync(att);
        var save = new MenuFlyoutItem { Text = "Speichern unter …", Icon = Icon("save") };
        save.Click += (_, _) => _ = SaveAttachmentAsync(att);
        menu.Items.Add(open);
        menu.Items.Add(save);
        menu.ShowAt(fe);
    }

    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "Anhang" : name;
    }

    private async Task OpenAttachmentAsync(MailAttachment att)
    {
        var fileName = SafeFileName(att.FileName);
        if (BlockedOpenExtensions.Contains(Path.GetExtension(fileName)))
        {
            ShowToast("Ausführbare Dateien werden aus Sicherheitsgründen nicht direkt geöffnet. Bitte speichern.", isError: true);
            return;
        }

        try
        {
            var dir = Path.Combine(TempDir, Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, fileName);
            await using (var fs = File.Create(path))
                await att.SaveToAsync(fs);

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowToast($"Anhang konnte nicht geöffnet werden: {ex.Message}", isError: true);
        }
    }

    private async Task SaveAttachmentAsync(MailAttachment att)
    {
        try
        {
            var fileName = SafeFileName(att.FileName);
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.Downloads,
                SuggestedFileName = Path.GetFileNameWithoutExtension(fileName)
            };
            var ext = Path.GetExtension(fileName);
            picker.FileTypeChoices.Add(ext.Length > 0 ? $"{ext.TrimStart('.').ToUpperInvariant()}-Datei" : "Datei",
                [ext.Length > 0 ? ext : ".dat"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance));

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            await using (var stream = await file.OpenStreamForWriteAsync())
            {
                stream.SetLength(0);
                await att.SaveToAsync(stream);
            }
            ShowToast($"Gespeichert: {file.Name}", "save");
        }
        catch (Exception ex)
        {
            ShowToast($"Speichern fehlgeschlagen: {ex.Message}", isError: true);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Message actions
    // ══════════════════════════════════════════════════════════════════

    private void ToggleRead_Click(object sender, RoutedEventArgs e)
    {
        if (_selected != null) _ = ToggleReadAsync(_selected);
    }

    private void Flag_Click(object sender, RoutedEventArgs e)
    {
        if (_selected != null) _ = ToggleFlagAsync(_selected);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selected != null) _ = DeleteMessageAsync(_selected);
    }

    private void Archive_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || _imap == null) return;
        var m = _selected;
        var imap = _imap;
        _ = RemoveMessageAsync(m, () => imap.MoveAsync(m.FolderTag, m.Uid, ImapService.Archive),
            "Archiviert", "archive");
    }

    private void Spam_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || _imap == null) return;
        var m = _selected;
        var imap = _imap;
        var toInbox = m.FolderTag == ImapService.Spam;
        _ = RemoveMessageAsync(m,
            () => imap.MoveAsync(m.FolderTag, m.Uid, toInbox ? ImapService.Inbox : ImapService.Spam),
            toInbox ? "In den Posteingang verschoben" : "Als Spam markiert",
            toInbox ? "shield-check" : "spam");
    }

    private async Task ToggleReadAsync(MailMessage m)
    {
        if (_imap == null) return;
        var imap = _imap;
        var newValue = !m.IsRead;

        m.IsRead = newValue;
        AdjustUnread(m.FolderTag, newValue ? -1 : +1);
        if (m == _selected) UpdateReaderToolbar(m);

        try
        {
            await imap.SetFlagAsync(m.FolderTag, m.Uid, MessageFlags.Seen, newValue);
        }
        catch (Exception ex)
        {
            m.IsRead = !newValue;
            AdjustUnread(m.FolderTag, newValue ? +1 : -1);
            if (m == _selected) UpdateReaderToolbar(m);
            ShowToast($"Status konnte nicht geändert werden: {Friendly(ex)}", isError: true);
        }
    }

    private async Task ToggleFlagAsync(MailMessage m)
    {
        if (_imap == null) return;
        var imap = _imap;
        var newValue = !m.IsFlagged;

        m.IsFlagged = newValue;
        if (_full != null && _full.Uid == m.Uid) _full.IsFlagged = newValue;
        if (m == _selected)
        {
            UpdateReaderToolbar(m);
            Motion.FadeIn(FlagDot, 0, fromScale: 0.5f);
        }

        try
        {
            await imap.SetFlagAsync(m.FolderTag, m.Uid, MessageFlags.Flagged, newValue);
            ShowToast(newValue ? "Markiert" : "Markierung entfernt", "flag");
        }
        catch (Exception ex)
        {
            m.IsFlagged = !newValue;
            if (m == _selected) UpdateReaderToolbar(m);
            ShowToast($"Markierung fehlgeschlagen: {Friendly(ex)}", isError: true);
        }
    }

    private async Task DeleteMessageAsync(MailMessage m)
    {
        if (_imap == null) return;
        var imap = _imap;
        var permanent = m.FolderTag == ImapService.Trash || !imap.HasFolder(ImapService.Trash);

        if (permanent && !await ConfirmAsync("Endgültig löschen?",
                $"„{m.Subject}“ wird unwiderruflich gelöscht.", "Löschen"))
            return;

        await RemoveMessageAsync(m, () => imap.DeleteAsync(m.FolderTag, m.Uid),
            permanent ? "Endgültig gelöscht" : "In den Papierkorb verschoben", "trash");
    }

    /// <summary>Optimistically removes a message from the list, then performs the server operation.</summary>
    private async Task RemoveMessageAsync(MailMessage m, Func<Task> serverOp, string toast, string glyph)
    {
        var listIndex = _messages.IndexOf(m);
        var allIndex = _all.IndexOf(m);
        var wasSelected = m == _selected;

        if (MailList.ContainerFromItem(m) is UIElement container)
            await Motion.FadeOut(container, 0, (float)Tokens.Double("MotionModalScale"));
        _messages.Remove(m);
        _all.Remove(m);
        _total = Math.Max(0, _total - 1);
        if (!m.IsRead) AdjustUnread(m.FolderTag, -1);

        if (wasSelected)
        {
            if (_messages.Count > 0 && listIndex >= 0)
                MailList.SelectedIndex = Math.Min(listIndex, _messages.Count - 1);
            else
                ClearReader();
        }

        UpdateFolderHeader();
        if (_messages.Count == 0) ApplyFilter();

        try
        {
            await serverOp();
            ShowToast(toast, glyph);
            _ = RefreshCountsAsync();
        }
        catch (Exception ex)
        {
            if (allIndex >= 0)
            {
                _all.Insert(Math.Min(allIndex, _all.Count), m);
                _total++;
                if (!m.IsRead) AdjustUnread(m.FolderTag, +1);
                ApplyFilter();
            }
            ShowToast($"Aktion fehlgeschlagen: {Friendly(ex)}", isError: true);
        }
    }

    private void AdjustUnread(string tag, int delta)
    {
        if (tag is not (ImapService.Inbox or ImapService.Spam)) return;
        var item = _folders.FirstOrDefault(f => f.Tag == tag);
        if (item == null) return;
        item.Count = Math.Max(0, item.Count + delta);
        if (tag == ImapService.Inbox) UpdateTrayCount();
        UpdateFolderHeader();
    }

    private void UpdateTrayCount() =>
        App.Instance?.Tray.SetUnreadCount(_folders.First(f => f.Tag == ImapService.Inbox).Count);

    private async Task<bool> ConfirmAsync(string title, string content, string primary)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primary,
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // ══════════════════════════════════════════════════════════════════
    //  Compose
    // ══════════════════════════════════════════════════════════════════

    private void Compose_Click(object sender, RoutedEventArgs e) => OpenNewCompose();

    public void OpenNewCompose() => OpenCompose(new ComposeRequest());

    private void OpenCompose(ComposeRequest request)
    {
        if (_account == null)
        {
            ShowToast("Bitte zuerst ein E-Mail-Konto hinzufügen.", isError: true);
            _ = AddAccountAsync();
            return;
        }

        if (request.DraftUid == null && request.Body.Length == 0 && _account.Signature.Length > 0)
            request.Body = $"\n\n-- \n{_account.Signature}";

        Composer.Open(_accounts, _account, request,
            canSaveDraft: _imap?.HasFolder(ImapService.Drafts) == true);

        _composeOpen = true;
        ComposeLayer.Visibility = Visibility.Visible;
        // Modal: fade + scale 0.95 → 1 + slight slide up; closing is the reverse.
        Motion.FadeTo(ComposeSmoke, 1, "MotionBase");
        Motion.FadeIn(Composer, (float)Tokens.Double("MotionModalSlide"),
            fromScale: (float)Tokens.Double("MotionModalScale"), duration: "MotionSlow");
    }

    private async Task CloseComposeAsync()
    {
        if (!_composeOpen) return;
        _composeOpen = false;
        Motion.FadeTo(ComposeSmoke, 0, "MotionBase");
        await Motion.FadeOut(Composer, (float)Tokens.Double("MotionModalSlide"),
            (float)Tokens.Double("MotionModalScale"), duration: "MotionBase");
        if (_composeOpen) return; // reopened meanwhile
        ComposeLayer.Visibility = Visibility.Collapsed;
        Motion.Reset(Composer);
        MailList.Focus(FocusState.Programmatic);
    }

    private async void Composer_Sent(object? sender, ComposeSentEventArgs e)
    {
        await CloseComposeAsync();
        ShowToast("E-Mail gesendet", "send");

        if (_imap == null || _account == null || e.Account.Id != _account.Id) return;
        var imap = _imap;

        try
        {
            if (!imap.ServerSavesSentMail && imap.HasFolder(ImapService.Sent))
                await imap.AppendAsync(ImapService.Sent, e.Message, MessageFlags.Seen);

            if (e.ReplacedDraftUid is uint draftUid)
                await imap.DeletePermanentAsync(ImapService.Drafts, draftUid);

            if (_folder is ImapService.Sent or ImapService.Drafts)
                await LoadFolderAsync(_folder, quiet: true);
            _ = RefreshCountsAsync();
        }
        catch (Exception ex)
        {
            ShowToast($"Gesendet, aber nicht im Ordner „Gesendet“ abgelegt: {Friendly(ex)}", isError: true);
        }
    }

    private async void Composer_DraftSaveRequested(object? sender, ComposeSentEventArgs e)
    {
        if (_imap == null) return;
        var imap = _imap;

        try
        {
            await imap.AppendAsync(ImapService.Drafts, e.Message, MessageFlags.Draft | MessageFlags.Seen);
            if (e.ReplacedDraftUid is uint draftUid)
                await imap.DeletePermanentAsync(ImapService.Drafts, draftUid);

            await CloseComposeAsync();
            ShowToast("Entwurf gespeichert", "draft");

            if (_folder == ImapService.Drafts)
                await LoadFolderAsync(_folder, quiet: true);
            _ = RefreshCountsAsync();
        }
        catch (Exception ex)
        {
            Composer.ShowError($"Entwurf konnte nicht gespeichert werden: {Friendly(ex)}");
        }
    }

    private void Reply_Click(object sender, RoutedEventArgs e) => StartReply(all: false);
    private void ReplyAll_Click(object sender, RoutedEventArgs e) => StartReply(all: true);
    private void Forward_Click(object sender, RoutedEventArgs e) => StartForward();

    private void EditDraft_Click(object sender, RoutedEventArgs e)
    {
        var m = _full;
        if (m == null) return;
        OpenCompose(new ComposeRequest
        {
            Title = "Entwurf bearbeiten",
            To = m.To,
            Cc = m.Cc,
            Subject = m.Subject == "(Kein Betreff)" ? "" : m.Subject,
            Body = m.PlainBody,
            Attachments = [.. m.Attachments],
            DraftUid = m.Uid,
            FocusBody = true
        });
    }

    private void StartReply(bool all)
    {
        var m = _full;
        if (m == null || _account == null) return;

        var to = m.ReplyTo.Length > 0 ? m.ReplyTo : m.From;
        var cc = "";

        if (all)
        {
            var own = _account.EmailAddress;
            var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { own };
            foreach (var mb in ParseMailboxes(to)) exclude.Add(mb.Address);

            var others = ParseMailboxes(m.To).Concat(ParseMailboxes(m.Cc))
                .Where(mb => exclude.Add(mb.Address))
                .Select(mb => mb.ToString());
            cc = string.Join(", ", others);
        }

        OpenCompose(new ComposeRequest
        {
            Title = all ? "Allen antworten" : "Antworten",
            To = to,
            Cc = cc,
            Subject = WithPrefix("AW: ", m.Subject, "re:", "aw:"),
            Body = "\n\n" + SignatureBlock() + Quote(m),
            InReplyTo = m.MessageId,
            References = [.. m.References, .. m.MessageId.Length > 0 ? new[] { m.MessageId } : []],
            FocusBody = true
        });
    }

    private void StartForward()
    {
        var m = _full;
        if (m == null) return;

        var header =
            "---------- Weitergeleitete Nachricht ----------\n" +
            $"Von: {m.From}\n" +
            $"Datum: {Fmt.LongDate(m.Date)}\n" +
            $"Betreff: {m.Subject}\n" +
            $"An: {m.To}\n" +
            (m.Cc.Length > 0 ? $"CC: {m.Cc}\n" : "") + "\n";

        OpenCompose(new ComposeRequest
        {
            Title = "Weiterleiten",
            Subject = WithPrefix("WG: ", m.Subject, "wg:", "fw:", "fwd:"),
            Body = "\n\n" + SignatureBlock() + header + m.PlainBody,
            Attachments = [.. m.Attachments]
        });
    }

    private string SignatureBlock() =>
        _account?.Signature.Length > 0 ? $"-- \n{_account.Signature}\n\n" : "";

    private static IEnumerable<MailboxAddress> ParseMailboxes(string text) =>
        InternetAddressList.TryParse(text, out var list) ? list.Mailboxes : [];

    private static string WithPrefix(string prefix, string subject, params string[] existing)
    {
        if (subject == "(Kein Betreff)") subject = "";
        var trimmed = subject.TrimStart();
        return existing.Any(p => trimmed.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            ? trimmed
            : prefix + trimmed;
    }

    private static string Quote(MailMessage m)
    {
        var sender = m.FromAddress.Length > 0 ? $"{m.FromDisplay} <{m.FromAddress}>" : m.FromDisplay;
        var lines = m.PlainBody.Replace("\r\n", "\n").Split('\n').Select(l => "> " + l);
        return $"Am {Fmt.LongDate(m.Date)} schrieb {sender}:\n{string.Join("\n", lines)}";
    }

    // ══════════════════════════════════════════════════════════════════
    //  Search & sync
    // ══════════════════════════════════════════════════════════════════

    public void SetLocalSearch(string text)
    {
        _localQuery = text.Trim();
        if (_localQuery.Length == 0 && _serverQuery != null)
        {
            _ = LoadFolderAsync(_folder);
            return;
        }
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    public async Task ServerSearchAsync(string query)
    {
        query = query.Trim();
        if (_imap == null) return;
        if (query.Length == 0)
        {
            _localQuery = "";
            await LoadFolderAsync(_folder);
            return;
        }

        var imap = _imap;
        var v = ++_loadVersion;
        _serverQuery = query;
        _localQuery = "";
        ShowState(ListState.Loading);
        ClearReader();
        UpdateFolderHeader();

        try
        {
            var results = await imap.SearchAsync(_folder, query);
            if (v != _loadVersion) return;
            _all = results;
            _total = results.Count;
            ApplyFilter();
            Motion.FadeIn(FolderHeader, 0, fromX: -8);
        }
        catch (Exception ex) when (v == _loadVersion)
        {
            ShowError($"Suche fehlgeschlagen: {Friendly(ex)}");
            ShowEmpty("Suche fehlgeschlagen", "Der Server unterstützt diese Suche evtl. nicht.", "alert");
        }
    }

    public async Task SyncNowAsync(bool manual = true)
    {
        if (_imap == null || _account == null || _syncing) return;
        var imap = _imap;
        var account = _account;
        var cv = _connectVersion;

        _syncing = true;
        SyncStateChanged?.Invoke(true);

        try
        {
            var (newMessages, maxUid) = await imap.GetNewInboxMessagesAsync(_lastInboxUid);
            if (cv != _connectVersion) return;

            if (_lastInboxUid != 0 && newMessages.Count > 0)
            {
                var window = App.MainWindowInstance;
                if (!window.AppWindow.IsVisible || !window.IsActiveWindow)
                    NotificationService.ShowNewMail(account.Name, newMessages);
            }
            _lastInboxUid = Math.Max(_lastInboxUid, maxUid);

            if (_serverQuery == null)
                await MergeLatestAsync(imap);

            await RefreshCountsAsync();
            if (manual) HideError();
        }
        catch (Exception ex) when (cv == _connectVersion)
        {
            if (manual) ShowToast($"Synchronisierung fehlgeschlagen: {Friendly(ex)}", isError: true);
        }
        catch
        {
            // account switched meanwhile
        }
        finally
        {
            _syncing = false;
            SyncStateChanged?.Invoke(false);
        }
    }

    /// <summary>Merges the newest page of the current folder into the list without a full reload.</summary>
    private async Task MergeLatestAsync(ImapService imap)
    {
        var v = _loadVersion;
        var folder = _folder;
        var (latest, total) = await imap.GetMessagesAsync(folder, 0, PageSize);
        if (v != _loadVersion || folder != _folder) return;

        _total = total;
        var existing = _all.GroupBy(m => m.Uid).ToDictionary(g => g.Key, g => g.First());
        var latestUids = latest.Select(m => m.Uid).ToHashSet();

        foreach (var m in latest)
        {
            if (!existing.TryGetValue(m.Uid, out var old)) continue;
            old.IsRead = m.IsRead;
            old.IsFlagged = m.IsFlagged;
        }

        var oldest = latest.Count > 0 ? latest.Min(m => m.Date) : DateTime.MaxValue;
        var removed = latest.Count == 0
            ? _all.ToList()
            : _all.Where(m => m.Date >= oldest && !latestUids.Contains(m.Uid)).ToList();
        foreach (var r in removed)
        {
            _all.Remove(r);
            _messages.Remove(r);
            if (r == _selected) ClearReader();
        }

        foreach (var n in latest.Where(m => !existing.ContainsKey(m.Uid)).OrderBy(m => m.Date))
        {
            var allIdx = _all.FindIndex(m => m.Date < n.Date);
            _all.Insert(allIdx < 0 ? _all.Count : allIdx, n);

            if (!Matches(n)) continue;
            var listIdx = -1;
            for (var i = 0; i < _messages.Count; i++)
            {
                if (_messages[i].Date < n.Date) { listIdx = i; break; }
            }
            _animateIn.Add(n);
            _messages.Insert(listIdx < 0 ? _messages.Count : listIdx, n);
        }

        UpdateFolderHeader();
        UpdateLoadMore();
        if (_messages.Count > 0) ShowState(ListState.List);
        else if (removed.Count > 0) ApplyFilter();
    }

    private async Task RefreshCountsAsync()
    {
        if (_imap == null) return;
        var imap = _imap;
        var cv = _connectVersion;

        try
        {
            var counts = await imap.GetUnreadCountsAsync();
            if (cv != _connectVersion) return;
            foreach (var f in _folders)
                f.Count = counts.TryGetValue(f.Tag, out var c) ? c : 0;
            UpdateTrayCount();
            UpdateFolderHeader();
        }
        catch { /* counts are cosmetic */ }
    }

    private void ApplySyncInterval()
    {
        var minutes = SettingsService.Current.SyncIntervalMinutes;
        _syncTimer.Stop();
        if (minutes <= 0) return;
        _syncTimer.Interval = TimeSpan.FromMinutes(minutes);
        _syncTimer.Start();
    }

    // ══════════════════════════════════════════════════════════════════
    //  Sidebar, settings, toast
    // ══════════════════════════════════════════════════════════════════

    public void TogglePane() => SetPaneCollapsed(!IsPaneCollapsed, animate: true);

    private void SetPaneCollapsed(bool collapsed, bool animate)
    {
        IsPaneCollapsed = collapsed;
        var width = collapsed ? SidebarCollapsedWidth : SidebarExpandedWidth;
        var labelVisibility = collapsed ? Visibility.Collapsed : Visibility.Visible;

        ComposeLabel.Visibility = labelVisibility;
        AccountTextPanel.Visibility = labelVisibility;
        AccountChevron.Visibility = labelVisibility;
        SettingsLabel.Visibility = labelVisibility;
        foreach (var f in _folders) f.IsCompact = collapsed;

        if (!animate)
        {
            Sidebar.Width = width;
            return;
        }

        var animation = new DoubleAnimation
        {
            To = width,
            Duration = TimeSpan.FromMilliseconds(280),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animation, Sidebar);
        Storyboard.SetTargetProperty(animation, "Width");
        var sb = new Storyboard();
        sb.Children.Add(animation);
        sb.Begin();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        _ = ShowSettingsAsync(SettingsHost.Visibility != Visibility.Visible);

    /// <summary>Page switch mail ↔ settings as a crossfade (never blocks input).</summary>
    private Task ShowSettingsAsync(bool show)
    {
        var isOpen = SettingsHost.Visibility == Visibility.Visible && SettingsHost.IsHitTestVisible;
        if (show == isOpen) return Task.CompletedTask;

        if (show)
        {
            SettingsPanel.Refresh();
            FolderList.SelectedItem = null;
            Motion.Crossfade(MailArea, SettingsHost);
            Svg.SetTone(SettingsIcon, "Primary");
        }
        else
        {
            Motion.Crossfade(SettingsHost, MailArea);
            Svg.SetTone(SettingsIcon, "Secondary");
            SelectFolderItem(_folder);
        }
        return Task.CompletedTask;
    }

    /// <summary>Toast slides in from the side with a draining progress bar.</summary>
    public void ShowToast(string text, string? icon = null, bool isError = false)
    {
        ToastText.Text = text;
        Svg.Set(ToastIcon, isError ? "alert" : icon ?? "success", isError ? "Danger" : "Primary");
        Toast.Visibility = Visibility.Visible;
        Motion.FadeIn(Toast, 0, fromX: 32);

        var duration = Tokens.Ms(isError ? "MotionToastError" : "MotionToast");
        Motion.Drain(ToastProgress, duration);
        _toastTimer.Stop();
        _toastTimer.Interval = duration;
        _toastTimer.Start();
    }

    private async Task HideToastAsync()
    {
        _toastTimer.Stop();
        await Motion.FadeOut(Toast, 0, toX: 32);
        if (_toastTimer.IsRunning) return; // a new toast appeared meanwhile
        Toast.Visibility = Visibility.Collapsed;
        Motion.Reset(Toast);
    }

    private static ImageIcon Icon(string kind)
    {
        var icon = new ImageIcon();
        Svg.SetKind(icon, kind);
        return icon;
    }

    // ══════════════════════════════════════════════════════════════════
    //  Keyboard shortcuts
    // ══════════════════════════════════════════════════════════════════

    private bool CanUseShortcut(KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_composeOpen) return false;
        args.Handled = true;
        return true;
    }

    private void Accel_Compose(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs a)
    {
        if (CanUseShortcut(a)) OpenNewCompose();
    }

    private void Accel_Sync(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs a)
    {
        if (CanUseShortcut(a)) _ = SyncNowAsync();
    }

    private void Accel_Reply(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs a)
    {
        if (CanUseShortcut(a)) StartReply(all: false);
    }

    private void Accel_ReplyAll(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs a)
    {
        if (CanUseShortcut(a)) StartReply(all: true);
    }

    private void Accel_Forward(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs a)
    {
        if (CanUseShortcut(a)) StartForward();
    }

    private void Accel_ToggleRead(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs a)
    {
        if (CanUseShortcut(a) && _selected != null) _ = ToggleReadAsync(_selected);
    }

    private void Accel_Search(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs a)
    {
        if (CanUseShortcut(a)) SearchFocusRequested?.Invoke();
    }

    // ══════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════

    public static string Friendly(Exception ex) => ex switch
    {
        AuthenticationException =>
            "Anmeldung fehlgeschlagen – Benutzername oder Passwort falsch. " +
            "(Gmail, Outlook, iCloud & Yahoo benötigen meist ein App-Passwort.)",
        SslHandshakeException =>
            "Sichere Verbindung fehlgeschlagen – Port und SSL/TLS-Einstellung prüfen.",
        SocketException =>
            "Server nicht erreichbar – Internetverbindung und Servername prüfen.",
        TimeoutException or OperationCanceledException =>
            "Zeitüberschreitung – der Server antwortet nicht.",
        FormatException fe => fe.Message,
        _ => ex.InnerException != null && ex.Message.Length < 8 ? Friendly(ex.InnerException) : ex.Message
    };

    public void Shutdown()
    {
        _syncTimer.Stop();
        ++_connectVersion;
        try { _imap?.Dispose(); } catch { }
        _imap = null;
        try { if (Directory.Exists(TempDir)) Directory.Delete(TempDir, true); } catch { }
    }
}
