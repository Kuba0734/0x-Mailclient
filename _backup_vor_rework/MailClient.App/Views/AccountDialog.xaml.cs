using MailClient.Core.Models;
using MailClient.Core.Services;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;

namespace MailClient.App.Views;

public sealed partial class AccountDialog : ContentDialog
{
    public MailAccount Result { get; private set; } = new();
    public string Password { get; private set; } = "";

    private readonly MailAccount? _existing;

    public AccountDialog(MailAccount? existing = null)
    {
        InitializeComponent();
        _existing = existing;

        if (existing != null)
        {
            Title = "Account bearbeiten";
            DisplayNameBox.Text = existing.DisplayName;
            EmailBox.Text = existing.EmailAddress;
            UsernameBox.Text = existing.Username;
            ImapHostBox.Text = existing.ImapHost;
            ImapPortBox.Text = existing.ImapPort.ToString();
            ImapSslToggle.IsOn = existing.ImapUseSsl;
            SmtpHostBox.Text = existing.SmtpHost;
            SmtpPortBox.Text = existing.SmtpPort.ToString();
            SmtpSslToggle.IsOn = existing.SmtpUseSsl;

            // Load existing password
            var pw = App.Accounts.LoadPassword(existing.Id);
            if (pw != null) PasswordBox.Password = pw;
        }
    }

    private async void Test_Click(
        ContentDialog sender,
        ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;

        if (!TryBuildAccount(out var account, out var password))
            return;

        IsSecondaryButtonEnabled = false;
        ShowInfo("Verbindung wird getestet...", InfoBarSeverity.Informational);

        try
        {
            using var imap = new ImapService();
            await imap.ConnectAsync(
                account.ImapHost, account.ImapPort, account.ImapUseSsl,
                account.Username, password);

            ShowInfo("Verbindung erfolgreich!", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowInfo($"Verbindung fehlgeschlagen: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            IsSecondaryButtonEnabled = true;
        }
    }

    private void Save_Click(
        ContentDialog sender,
        ContentDialogButtonClickEventArgs args)
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
        password = "";

        var email = EmailBox.Text.Trim();
        var username = UsernameBox.Text.Trim();
        password = PasswordBox.Password;
        var imapHost = ImapHostBox.Text.Trim();
        var smtpHost = SmtpHostBox.Text.Trim();

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(imapHost) ||
            string.IsNullOrEmpty(smtpHost) || string.IsNullOrEmpty(password))
        {
            ShowInfo("Bitte alle Pflichtfelder ausfüllen.", InfoBarSeverity.Warning);
            return false;
        }

        if (!int.TryParse(ImapPortBox.Text, out var imapPort)) imapPort = 993;
        if (!int.TryParse(SmtpPortBox.Text, out var smtpPort)) smtpPort = 587;

        account = new MailAccount
        {
            Id = _existing?.Id ?? Guid.NewGuid(),
            DisplayName = DisplayNameBox.Text.Trim(),
            EmailAddress = email,
            Username = username.Length > 0 ? username : email,
            ImapHost = imapHost,
            ImapPort = imapPort,
            ImapUseSsl = ImapSslToggle.IsOn,
            SmtpHost = smtpHost,
            SmtpPort = smtpPort,
            SmtpUseSsl = SmtpSslToggle.IsOn
        };

        return true;
    }

    private void EmailBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var email = EmailBox.Text.Trim();
        var atIdx = email.IndexOf('@');
        if (atIdx < 0) return;

        var domain = email[(atIdx + 1)..].ToLowerInvariant();
        if (!KnownProviders.TryGetValue(domain, out var cfg)) return;

        // Nur ausfüllen wenn noch leer oder bereits auto-befüllt
        if (string.IsNullOrWhiteSpace(ImapHostBox.Text) || _autoFilled)
        {
            ImapHostBox.Text = cfg.ImapHost;
            ImapPortBox.Text = cfg.ImapPort.ToString();
            ImapSslToggle.IsOn = cfg.ImapSsl;
            SmtpHostBox.Text = cfg.SmtpHost;
            SmtpPortBox.Text = cfg.SmtpPort.ToString();
            SmtpSslToggle.IsOn = cfg.SmtpSsl;
            _autoFilled = true;
        }
    }

    private bool _autoFilled;

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

    private void ShowInfo(string message, InfoBarSeverity severity)
    {
        DialogInfoBar.Message = message;
        DialogInfoBar.Severity = severity;
        DialogInfoBar.IsOpen = true;
    }
}
