using MailClient.App.Helpers;
using MailClient.Core.Models;
using MailClient.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MimeKit;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace MailClient.App.Views;

public sealed class ComposeSentEventArgs(MailAccount account, MimeMessage message, uint? replacedDraftUid) : EventArgs
{
    public MailAccount Account { get; } = account;
    public MimeMessage Message { get; } = message;
    public uint? ReplacedDraftUid { get; } = replacedDraftUid;
}

public sealed partial class ComposePanel : UserControl
{
    public event EventHandler? CloseRequested;
    public event EventHandler<ComposeSentEventArgs>? Sent;
    public event EventHandler<ComposeSentEventArgs>? DraftSaveRequested;

    private readonly ObservableCollection<MailAttachment> _attachments = [];
    private List<MailAccount> _accounts = [];
    private ComposeRequest _request = new();
    private string _snapshot = "";
    private bool _busy;
    private bool _canSaveDraft;

    public ComposePanel()
    {
        InitializeComponent();
        AttachmentsRepeater.ItemsSource = _attachments;
        _attachments.CollectionChanged += (_, _) =>
            AttachmentsScroller.Visibility = _attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        AnimationHelper.EnableImplicitFade(CcRow, -6);
        AnimationHelper.EnableImplicitFade(BccRow, -6);
        AnimationHelper.EnableImplicitFade(AttachmentsScroller, 10);

        Card.Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow();
        Card.Translation = new Vector3(0, 0, 48);
    }

    public void SetShadowReceiver(UIElement receiver)
    {
        if (Card.Shadow is Microsoft.UI.Xaml.Media.ThemeShadow shadow && !shadow.Receivers.Contains(receiver))
            shadow.Receivers.Add(receiver);
    }

    public void Open(IReadOnlyList<MailAccount> accounts, MailAccount current, ComposeRequest request, bool canSaveDraft)
    {
        _accounts = [.. accounts];
        _request = request;
        _canSaveDraft = canSaveDraft;

        TitleText.Text = request.Title;
        HeaderGlyph.Glyph = request.Title switch
        {
            "Weiterleiten" => "",
            "Antworten" => "",
            "Allen antworten" => "",
            _ => ""
        };

        FromCombo.ItemsSource = _accounts.Select(a => $"{a.Name} <{a.EmailAddress}>").ToList();
        FromCombo.SelectedIndex = Math.Max(0, _accounts.FindIndex(a => a.Id == current.Id));
        var multipleAccounts = _accounts.Count > 1;
        FromCombo.Visibility = multipleAccounts ? Visibility.Visible : Visibility.Collapsed;
        FromText.Visibility = multipleAccounts ? Visibility.Collapsed : Visibility.Visible;
        FromText.Text = $"{current.Name} <{current.EmailAddress}>";

        ToBox.Text = request.To;
        CcBox.Text = request.Cc;
        BccBox.Text = request.Bcc;
        SubjectBox.Text = request.Subject;
        BodyBox.Text = request.Body;

        var showCc = request.Cc.Length > 0 || request.Bcc.Length > 0;
        CcRow.Visibility = showCc ? Visibility.Visible : Visibility.Collapsed;
        BccRow.Visibility = showCc ? Visibility.Visible : Visibility.Collapsed;
        CcToggle.Visibility = showCc ? Visibility.Collapsed : Visibility.Visible;

        _attachments.Clear();
        foreach (var a in request.Attachments) _attachments.Add(a);

        DraftButton.Visibility = canSaveDraft ? Visibility.Visible : Visibility.Collapsed;
        ErrorBar.IsOpen = false;
        SetBusy(false);
        _snapshot = Snapshot();

        DispatcherQueue.TryEnqueue(() =>
        {
            if (request.FocusBody)
            {
                BodyBox.Focus(FocusState.Programmatic);
                BodyBox.SelectionStart = 0;
            }
            else if (ToBox.Text.Length == 0)
            {
                ToBox.Focus(FocusState.Programmatic);
            }
            else
            {
                SubjectBox.Focus(FocusState.Programmatic);
            }
        });
    }

    public void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private MailAccount SelectedAccount =>
        _accounts.Count > 1 && FromCombo.SelectedIndex >= 0
            ? _accounts[FromCombo.SelectedIndex]
            : _accounts[Math.Max(0, FromCombo.SelectedIndex)];

    private string Snapshot() =>
        string.Join("\u0001", ToBox.Text, CcBox.Text, BccBox.Text, SubjectBox.Text, BodyBox.Text, _attachments.Count);

    private bool IsDirty => Snapshot() != _snapshot;

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SendButton.IsEnabled = !busy;
        DraftButton.IsEnabled = !busy;
        SendRing.IsActive = busy;
        SendContent.Opacity = busy ? 0 : 1;
    }

    private OutgoingMail BuildOutgoing() => new()
    {
        To = ToBox.Text.Trim(),
        Cc = CcBox.Text.Trim(),
        Bcc = BccBox.Text.Trim(),
        Subject = SubjectBox.Text.Trim(),
        TextBody = BodyBox.Text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n"),
        InReplyTo = _request.InReplyTo,
        References = _request.References,
        AttachmentPaths = _attachments.Where(a => a.LocalPath != null).Select(a => a.LocalPath!).ToList(),
        ForwardedAttachments = _attachments.Where(a => a.LocalPath == null && a.Entity != null).ToList()
    };

    // ── Send ─────────────────────────────────────────────────────────────

    private void Send_Click(object sender, RoutedEventArgs e) => _ = SendAsync();

    private async Task SendAsync()
    {
        if (_busy) return;
        ErrorBar.IsOpen = false;

        if (string.IsNullOrWhiteSpace(ToBox.Text) && string.IsNullOrWhiteSpace(CcBox.Text) &&
            string.IsNullOrWhiteSpace(BccBox.Text))
        {
            ShowError("Bitte mindestens einen Empfänger angeben.");
            AnimationHelper.Shake(ToBox);
            ToBox.Focus(FocusState.Programmatic);
            return;
        }

        if (string.IsNullOrWhiteSpace(SubjectBox.Text))
        {
            var confirm = new ContentDialog
            {
                Title = "Ohne Betreff senden?",
                Content = "Diese Nachricht hat keinen Betreff.",
                PrimaryButtonText = "Trotzdem senden",
                CloseButtonText = "Abbrechen",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                SubjectBox.Focus(FocusState.Programmatic);
                return;
            }
        }

        var account = SelectedAccount;
        var password = App.Accounts.LoadPassword(account.Id);
        if (password == null)
        {
            ShowError("Für dieses Konto ist kein Passwort gespeichert. Bitte das Konto in den Einstellungen bearbeiten.");
            return;
        }

        SetBusy(true);
        try
        {
            var outgoing = BuildOutgoing();
            var message = await Task.Run(() => SmtpService.BuildMessage(account, outgoing));
            await new SmtpService().SendAsync(account, password, message);

            _snapshot = Snapshot();
            Sent?.Invoke(this, new ComposeSentEventArgs(account, message, _request.DraftUid));
        }
        catch (Exception ex)
        {
            ShowError($"Senden fehlgeschlagen: {MainPage.Friendly(ex)}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ── Drafts ───────────────────────────────────────────────────────────

    private void SaveDraft_Click(object sender, RoutedEventArgs e) => _ = SaveDraftAsync();

    private async Task SaveDraftAsync()
    {
        if (_busy || !_canSaveDraft) return;
        ErrorBar.IsOpen = false;
        SetBusy(true);
        try
        {
            var account = SelectedAccount;
            var outgoing = BuildOutgoing();
            var message = await Task.Run(() => SmtpService.BuildMessage(account, outgoing));
            _snapshot = Snapshot();
            DraftSaveRequested?.Invoke(this, new ComposeSentEventArgs(account, message, _request.DraftUid));
        }
        catch (Exception ex)
        {
            ShowError($"Entwurf konnte nicht erstellt werden: {MainPage.Friendly(ex)}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ── Closing ──────────────────────────────────────────────────────────

    private void Close_Click(object sender, RoutedEventArgs e) => _ = RequestCloseAsync();

    private async Task RequestCloseAsync()
    {
        if (_busy) return;

        if (IsDirty)
        {
            var dialog = new ContentDialog
            {
                Title = "Nachricht verwerfen?",
                Content = _canSaveDraft
                    ? "Du hast Änderungen, die noch nicht gesendet wurden."
                    : "Deine Änderungen gehen verloren.",
                PrimaryButtonText = _canSaveDraft ? "Als Entwurf speichern" : "",
                SecondaryButtonText = "Verwerfen",
                CloseButtonText = "Weiter bearbeiten",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme
            };

            switch (await dialog.ShowAsync())
            {
                case ContentDialogResult.Primary:
                    await SaveDraftAsync();
                    return;
                case ContentDialogResult.None:
                    return;
            }
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Accel_Send(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = SendAsync();
    }

    private void Accel_Close(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = RequestCloseAsync();
    }

    private void CcToggle_Click(object sender, RoutedEventArgs e)
    {
        CcRow.Visibility = Visibility.Visible;
        BccRow.Visibility = Visibility.Visible;
        CcToggle.Visibility = Visibility.Collapsed;
        CcBox.Focus(FocusState.Programmatic);
    }

    // ── Attachments ──────────────────────────────────────────────────────

    private async void Attach_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance));

            var files = await picker.PickMultipleFilesAsync();
            await AddFilesAsync(files);
        }
        catch (Exception ex)
        {
            ShowError($"Datei konnte nicht angehängt werden: {ex.Message}");
        }
    }

    private async Task AddFilesAsync(IEnumerable<IStorageItem> items)
    {
        foreach (var file in items.OfType<StorageFile>())
        {
            if (_attachments.Any(a => string.Equals(a.LocalPath, file.Path, StringComparison.OrdinalIgnoreCase)))
                continue;

            ulong size = 0;
            try { size = (await file.GetBasicPropertiesAsync()).Size; } catch { }

            _attachments.Add(new MailAttachment
            {
                FileName = file.Name,
                LocalPath = file.Path,
                ContentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType,
                Size = (long)size
            });
        }

        var total = _attachments.Sum(a => a.Size);
        if (total > 25L * 1024 * 1024)
            ShowError($"Achtung: Die Anhänge sind zusammen {MailAttachment.FormatSize(total)} groß. Viele Server akzeptieren maximal 25 MB.");
    }

    private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MailAttachment att)
            _attachments.Remove(att);
    }

    private void Card_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Anhängen";
        DropHint.Opacity = 1;
    }

    private void Card_DragLeave(object sender, DragEventArgs e) => DropHint.Opacity = 0;

    private async void Card_Drop(object sender, DragEventArgs e)
    {
        DropHint.Opacity = 0;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            await AddFilesAsync(items);
        }
        catch (Exception ex)
        {
            ShowError($"Dateien konnten nicht angehängt werden: {ex.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }
}
