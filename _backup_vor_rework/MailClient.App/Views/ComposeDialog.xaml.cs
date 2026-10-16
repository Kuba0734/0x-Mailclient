using MailClient.Core.Models;
using MailClient.Core.Services;
using Microsoft.UI.Xaml.Controls;
using System;

namespace MailClient.App.Views;

public sealed partial class ComposeDialog : ContentDialog
{
    private readonly MailAccount _account;

    public ComposeDialog(
        MailAccount account,
        string to = "",
        string subject = "",
        string body = "")
    {
        InitializeComponent();
        _account = account;

        FromBox.Text = $"{account.DisplayName} <{account.EmailAddress}>";
        ToBox.Text = to;
        SubjectBox.Text = subject;
        BodyBox.Text = body;
    }

    private async void Send_Click(
        ContentDialog sender,
        ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; // Prevent dialog from closing until done

        var to = ToBox.Text.Trim();
        var subject = SubjectBox.Text.Trim();
        var body = BodyBox.Text.Trim();

        if (string.IsNullOrEmpty(to))
        {
            ShowError("Bitte einen Empfänger angeben.");
            return;
        }

        var password = App.Accounts.LoadPassword(_account.Id);
        if (password == null)
        {
            ShowError("Passwort nicht gefunden. Bitte Account neu konfigurieren.");
            return;
        }

        IsPrimaryButtonEnabled = false;
        SendInfoBar.IsOpen = false;

        try
        {
            var smtp = new SmtpService();
            await smtp.SendAsync(
                fromEmail: _account.EmailAddress,
                fromName: _account.DisplayName,
                to: to,
                cc: CcBox.Text.Trim(),
                subject: subject.Length > 0 ? subject : "(Kein Betreff)",
                htmlBody: "",
                textBody: body,
                smtpHost: _account.SmtpHost,
                smtpPort: _account.SmtpPort,
                ssl: _account.SmtpUseSsl,
                username: _account.Username,
                password: password);

            args.Cancel = false; // Allow close
            Hide();
        }
        catch (Exception ex)
        {
            ShowError($"Senden fehlgeschlagen: {ex.Message}");
            IsPrimaryButtonEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        SendInfoBar.Message = message;
        SendInfoBar.Severity = InfoBarSeverity.Error;
        SendInfoBar.IsOpen = true;
    }
}
