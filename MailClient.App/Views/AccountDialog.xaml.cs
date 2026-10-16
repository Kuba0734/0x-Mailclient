using MailClient.Core.Models;
using MailClient.Core.Services;
using MailKit.Net.Smtp;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MailClient.App.Views;

public sealed partial class AccountDialog : ContentDialog
{
    public MailAccount Result { get; private set; } = new();
    public string Password { get; private set; } = "";

    private readonly MailAccount? _existing;
    private bool _autoFilled;

    public AccountDialog(MailAccount? existing = null)
    {
        InitializeComponent();
        _existing = existing;

        if (existing != null)
        {
            TitleText.Text = "Konto bearbeiten";
            DisplayNameBox.Text = existing.DisplayName;
            EmailBox.Text = existing.EmailAddress;
            UsernameBox.Text = existing.Username == existing.EmailAddress ? "" : existing.Username;
            ImapHostBox.Text = existing.ImapHost;
            ImapPortBox.Value = existing.ImapPort;
            ImapSslToggle.IsOn = existing.ImapUseSsl;
            SmtpHostBox.Text = existing.SmtpHost;
            SmtpPortBox.Value = existing.SmtpPort;
            SmtpSslToggle.IsOn = existing.SmtpUseSsl;
            SignatureBox.Text = existing.Signature;

            var pw = App.Accounts.LoadPassword(existing.Id);
            if (pw != null) PasswordBox.Password = pw;
        }
        else
        {
            ServerExpander.IsExpanded = true;
        }

        UpdateServerSummary();
        ImapHostBox.TextChanged += (_, _) => UpdateServerSummary();
        SmtpHostBox.TextChanged += (_, _) => UpdateServerSummary();
    }

    // ── Static entry points ──────────────────────────────────────────────

    /// <summary>Shows the dialog to add a new account and persists it.</summary>
    public static async Task<MailAccount?> AddAsync(XamlRoot root)
    {
        var dialog = new AccountDialog { XamlRoot = root, RequestedTheme = ThemeOf(root) };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        var accounts = App.Accounts.LoadAccounts();
        accounts.Add(dialog.Result);
        App.Accounts.SaveAccounts(accounts);
        App.Accounts.SavePassword(dialog.Result.Id, dialog.Password);
        return dialog.Result;
    }

    /// <summary>Shows the dialog to edit <paramref name="account"/> and persists the changes.</summary>
    public static async Task<bool> EditAsync(XamlRoot root, MailAccount account)
    {
        var dialog = new AccountDialog(account) { XamlRoot = root, RequestedTheme = ThemeOf(root) };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;

        var accounts = App.Accounts.LoadAccounts();
        var idx = accounts.FindIndex(a => a.Id == account.Id);
        if (idx >= 0) accounts[idx] = dialog.Result;
        else accounts.Add(dialog.Result);
        App.Accounts.SaveAccounts(accounts);
        if (!string.IsNullOrEmpty(dialog.Password))
            App.Accounts.SavePassword(dialog.Result.Id, dialog.Password);
        return true;
    }

    private static ElementTheme ThemeOf(XamlRoot root) =>
        (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default;

    // ── Buttons ──────────────────────────────────────────────────────────

    private async void Test_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (!TryBuildAccount(out var account, out var password)) return;

        IsSecondaryButtonEnabled = false;
        IsPrimaryButtonEnabled = false;
        ShowInfo("Verbindung zum Posteingang (IMAP) wird getestet …", InfoBarSeverity.Informational);

        try
        {
            using (var imap = new ImapService())
            {
                await imap.ConnectAsync(account.ImapHost, account.ImapPort, account.ImapUseSsl,
                    account.Username, password);
            }

            ShowInfo("IMAP OK – Postausgang (SMTP) wird getestet …", InfoBarSeverity.Informational);

            using (var smtp = new SmtpClient { Timeout = 30_000 })
            {
                await smtp.ConnectAsync(account.SmtpHost, account.SmtpPort,
                    SmtpService.GetSocketOptions(account.SmtpPort, account.SmtpUseSsl));
                await smtp.AuthenticateAsync(account.Username, password);
                await smtp.DisconnectAsync(true);
            }

            ShowInfo("Alles bereit! Empfangen und Senden funktionieren.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowInfo($"Verbindung fehlgeschlagen: {MainPage.Friendly(ex)}", InfoBarSeverity.Error);
        }
        finally
        {
            IsSecondaryButtonEnabled = true;
            IsPrimaryButtonEnabled = true;
        }
    }

    private void Save_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!TryBuildAccount(out var account, out var password))
        {
            args.Cancel = true;
            return;
        }

        Result = account;
        Password = password;
    }

    private bool TryBuildAccount(out MailAccount account, out string password)
    {
        account = new MailAccount();
        password = PasswordBox.Password;

        var email = EmailBox.Text.Trim();
        var username = UsernameBox.Text.Trim();
        var imapHost = ImapHostBox.Text.Trim();
        var smtpHost = SmtpHostBox.Text.Trim();

        if (!email.Contains('@'))
        {
            ShowInfo("Bitte eine gültige E-Mail-Adresse eingeben.", InfoBarSeverity.Warning);
            EmailBox.Focus(FocusState.Programmatic);
            return false;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowInfo("Bitte das Passwort eingeben.", InfoBarSeverity.Warning);
            PasswordBox.Focus(FocusState.Programmatic);
            return false;
        }

        if (string.IsNullOrEmpty(imapHost) || string.IsNullOrEmpty(smtpHost))
        {
            ServerExpander.IsExpanded = true;
            ShowInfo("Bitte IMAP- und SMTP-Server angeben.", InfoBarSeverity.Warning);
            return false;
        }

        account = new MailAccount
        {
            Id = _existing?.Id ?? Guid.NewGuid(),
            DisplayName = DisplayNameBox.Text.Trim(),
            EmailAddress = email,
            Username = username.Length > 0 ? username : email,
            ImapHost = imapHost,
            ImapPort = double.IsNaN(ImapPortBox.Value) ? 993 : (int)ImapPortBox.Value,
            ImapUseSsl = ImapSslToggle.IsOn,
            SmtpHost = smtpHost,
            SmtpPort = double.IsNaN(SmtpPortBox.Value) ? 587 : (int)SmtpPortBox.Value,
            SmtpUseSsl = SmtpSslToggle.IsOn,
            Signature = SignatureBox.Text.TrimEnd()
        };
        return true;
    }

    // ── Provider auto-detection ──────────────────────────────────────────

    private void EmailBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var email = EmailBox.Text.Trim();
        var atIdx = email.IndexOf('@');
        if (atIdx < 0)
        {
            ProviderHint.IsOpen = false;
            return;
        }

        var domain = email[(atIdx + 1)..].ToLowerInvariant();
        UpdateProviderHint(domain);

        if (!KnownProviders.TryGetValue(domain, out var cfg)) return;

        // Only fill when empty or previously auto-filled
        if (string.IsNullOrWhiteSpace(ImapHostBox.Text) || _autoFilled)
        {
            ImapHostBox.Text = cfg.ImapHost;
            ImapPortBox.Value = cfg.ImapPort;
            ImapSslToggle.IsOn = cfg.ImapSsl;
            SmtpHostBox.Text = cfg.SmtpHost;
            SmtpPortBox.Value = cfg.SmtpPort;
            SmtpSslToggle.IsOn = cfg.SmtpSsl;
            _autoFilled = true;
            if (_existing == null) ServerExpander.IsExpanded = false;
        }
    }

    private void UpdateProviderHint(string domain)
    {
        string? hint = domain switch
        {
            "gmail.com" or "googlemail.com" =>
                "Google: Bitte ein App-Passwort verwenden (Google-Konto › Sicherheit › Bestätigung in zwei Schritten › App-Passwörter).",
            "icloud.com" or "me.com" or "mac.com" =>
                "iCloud: Bitte ein app-spezifisches Passwort unter appleid.apple.com erstellen.",
            "yahoo.com" or "yahoo.de" or "aol.com" or "aol.de" =>
                "Yahoo/AOL: Bitte ein App-Passwort in den Kontosicherheits-Einstellungen erstellen.",
            "gmx.de" or "gmx.at" or "gmx.ch" or "gmx.net" or "gmx.com" or "web.de" =>
                "GMX/Web.de: Der IMAP-Zugriff muss in den Einstellungen des Webmailers aktiviert sein.",
            "protonmail.com" or "proton.me" =>
                "Proton: Funktioniert nur mit laufender Proton Mail Bridge (Zugangsdaten aus der Bridge verwenden).",
            "outlook.com" or "hotmail.com" or "hotmail.de" or "live.com" or "live.de" or "msn.com" =>
                "Microsoft: Mit aktivierter Bestätigung in zwei Schritten wird ein App-Kennwort benötigt.",
            _ => null
        };

        ProviderHint.Message = hint ?? "";
        ProviderHint.IsOpen = hint != null;
    }

    private void ImapSsl_Toggled(object sender, RoutedEventArgs e)
    {
        // Switch between the common default ports when the user toggles encryption.
        if (ImapSslToggle.IsOn && ImapPortBox.Value == 143) ImapPortBox.Value = 993;
        else if (!ImapSslToggle.IsOn && ImapPortBox.Value == 993) ImapPortBox.Value = 143;
    }

    private void UpdateServerSummary()
    {
        var imap = ImapHostBox.Text.Trim();
        var smtp = SmtpHostBox.Text.Trim();
        ServerSummary.Text = imap.Length == 0 && smtp.Length == 0
            ? "IMAP & SMTP"
            : $"{(imap.Length > 0 ? imap : "–")}  ·  {(smtp.Length > 0 ? smtp : "–")}";
    }

    private void ShowInfo(string message, InfoBarSeverity severity)
    {
        DialogInfoBar.Message = message;
        DialogInfoBar.Severity = severity;
        DialogInfoBar.IsOpen = true;
    }

    private record ProviderConfig(
        string ImapHost, int ImapPort, bool ImapSsl,
        string SmtpHost, int SmtpPort, bool SmtpSsl);

    private static readonly Dictionary<string, ProviderConfig> KnownProviders = new()
    {
        // Google
        ["gmail.com"]       = new("imap.gmail.com",           993, true,  "smtp.gmail.com",          587, true),
        ["googlemail.com"]  = new("imap.gmail.com",           993, true,  "smtp.gmail.com",          587, true),
        // Microsoft
        ["outlook.com"]     = new("outlook.office365.com",    993, true,  "smtp-mail.outlook.com",   587, true),
        ["hotmail.com"]     = new("outlook.office365.com",    993, true,  "smtp-mail.outlook.com",   587, true),
        ["hotmail.de"]      = new("outlook.office365.com",    993, true,  "smtp-mail.outlook.com",   587, true),
        ["live.com"]        = new("outlook.office365.com",    993, true,  "smtp-mail.outlook.com",   587, true),
        ["live.de"]         = new("outlook.office365.com",    993, true,  "smtp-mail.outlook.com",   587, true),
        ["msn.com"]         = new("outlook.office365.com",    993, true,  "smtp-mail.outlook.com",   587, true),
        // Yahoo
        ["yahoo.com"]       = new("imap.mail.yahoo.com",      993, true,  "smtp.mail.yahoo.com",     587, true),
        ["yahoo.de"]        = new("imap.mail.yahoo.com",      993, true,  "smtp.mail.yahoo.com",     587, true),
        // Apple
        ["icloud.com"]      = new("imap.mail.me.com",         993, true,  "smtp.mail.me.com",        587, true),
        ["me.com"]          = new("imap.mail.me.com",         993, true,  "smtp.mail.me.com",        587, true),
        ["mac.com"]         = new("imap.mail.me.com",         993, true,  "smtp.mail.me.com",        587, true),
        // GMX
        ["gmx.de"]          = new("imap.gmx.net",             993, true,  "mail.gmx.net",            587, true),
        ["gmx.at"]          = new("imap.gmx.net",             993, true,  "mail.gmx.net",            587, true),
        ["gmx.ch"]          = new("imap.gmx.net",             993, true,  "mail.gmx.net",            587, true),
        ["gmx.net"]         = new("imap.gmx.net",             993, true,  "mail.gmx.net",            587, true),
        ["gmx.com"]         = new("imap.gmx.com",             993, true,  "mail.gmx.com",            587, true),
        // Web.de
        ["web.de"]          = new("imap.web.de",              993, true,  "smtp.web.de",             587, true),
        // T-Online
        ["t-online.de"]     = new("secureimap.t-online.de",   993, true,  "securesmtp.t-online.de",  465, true),
        // Posteo
        ["posteo.de"]       = new("posteo.de",                993, true,  "posteo.de",               587, true),
        ["posteo.net"]      = new("posteo.net",               993, true,  "posteo.net",              587, true),
        // ProtonMail (Bridge)
        ["protonmail.com"]  = new("127.0.0.1",                1143, false, "127.0.0.1",              1025, false),
        ["proton.me"]       = new("127.0.0.1",                1143, false, "127.0.0.1",              1025, false),
        // Mailbox.org
        ["mailbox.org"]     = new("imap.mailbox.org",         993, true,  "smtp.mailbox.org",        587, true),
        // Fastmail
        ["fastmail.com"]    = new("imap.fastmail.com",        993, true,  "smtp.fastmail.com",       587, true),
        ["fastmail.fm"]     = new("imap.fastmail.com",        993, true,  "smtp.fastmail.com",       587, true),

        // Freenet
        ["freenet.de"]      = new("imap.freenet.de",          993, true,  "smtp.freenet.de",         587, true),

        // 1&1 / IONOS
        ["1und1.de"]        = new("imap.1und1.de",            993, true,  "smtp.1und1.de",           587, true),
        ["ionos.de"]        = new("imap.ionos.de",            993, true,  "smtp.ionos.de",           587, true),
        ["mail.de"]         = new("imap.mail.de",             993, true,  "smtp.mail.de",            587, true),

        // AOL
        ["aol.com"]         = new("imap.aol.com",             993, true,  "smtp.aol.com",            587, true),
        ["aol.de"]          = new("imap.aol.com",             993, true,  "smtp.aol.com",            587, true),

        // Zoho
        ["zoho.com"]        = new("imap.zoho.com",            993, true,  "smtp.zoho.com",           587, true),
        ["zohomail.com"]    = new("imap.zoho.com",            993, true,  "smtp.zoho.com",           587, true),

        // Tutanota
        ["tutanota.com"]    = new("mail.tutanota.com",        993, true,  "mail.tutanota.com",       587, true),
        ["tuta.io"]         = new("mail.tutanota.com",        993, true,  "mail.tutanota.com",       587, true),

        // Runbox
        ["runbox.com"]      = new("mail.runbox.com",          993, true,  "mail.runbox.com",         587, true),

        // Yandex
        ["yandex.com"]      = new("imap.yandex.com",         993, true,  "smtp.yandex.com",         587, true),
        ["yandex.ru"]       = new("imap.yandex.ru",          993, true,  "smtp.yandex.ru",          587, true),
        ["ya.ru"]           = new("imap.yandex.ru",          993, true,  "smtp.yandex.ru",          587, true),

        // Mail.ru
        ["mail.ru"]         = new("imap.mail.ru",             993, true,  "smtp.mail.ru",            465, true),
        ["inbox.ru"]        = new("imap.mail.ru",             993, true,  "smtp.mail.ru",            465, true),
        ["list.ru"]         = new("imap.mail.ru",             993, true,  "smtp.mail.ru",            465, true),
        ["bk.ru"]           = new("imap.mail.ru",             993, true,  "smtp.mail.ru",            465, true),

        // O2 / Telefónica
        ["o2online.de"]     = new("imap.o2online.de",         993, true,  "smtp.o2online.de",        587, true),

        // Vodafone
        ["vodafone.de"]     = new("imap.vodafone.de",         993, true,  "smtp.vodafone.de",        587, true),

        // Arcor (Vodafone)
        ["arcor.de"]        = new("imap.arcor.de",            993, true,  "smtp.arcor.de",           587, true),

        // Alice / O2
        ["alice.de"]        = new("imap.alice.de",            993, true,  "smtp.alice.de",           587, true),

        // Kabelbw / BW-Futur / SWR
        ["kabelbw.de"]      = new("imap.kabelbw.de",          993, true,  "smtp.kabelbw.de",         587, true),

        // Fastlane / Telekom
        ["telekom.de"]      = new("secureimap.t-online.de",   993, true,  "securesmtp.t-online.de",  465, true),
        ["magenta.de"]      = new("secureimap.t-online.de",   993, true,  "securesmtp.t-online.de",  465, true),

        // Strato
        ["strato.de"]       = new("imap.strato.de",           993, true,  "smtp.strato.de",          587, true),

        // Hetzner
        ["hetzner.com"]     = new("imap.your-server.de",      993, true,  "mail.your-server.de",     587, true),

        // Disroot
        ["disroot.org"]     = new("disroot.org",              993, true,  "disroot.org",             587, true),

        // Kolab Now
        ["kolabnow.com"]    = new("imap.kolabnow.com",        993, true,  "smtp.kolabnow.com",       587, true),
    };
}
