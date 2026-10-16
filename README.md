# MailClient

E-Mail-Programm für Windows mit WinUI 3.

## Funktionen

- Mehrere Konten über IMAP und SMTP (MailKit/MimeKit)
- Mails lesen und verfassen
- Benachrichtigungen bei neuen Mails
- Tray-Symbol und Autostart

## Projekte

| Projekt | Inhalt |
|---|---|
| `MailClient.Core` | Modelle, IMAP- und SMTP-Dienste (.NET 8) |
| `MailClient.App` | WinUI-3-Oberfläche (Windows App SDK, CommunityToolkit) |

## Bauen

`MailClient.slnx` in Visual Studio 2022 öffnen (Workload „WinUI-Anwendungsentwicklung“) und `MailClient.App` starten.
