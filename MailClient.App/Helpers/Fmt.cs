using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using System;
using System.Globalization;

namespace MailClient.App.Helpers;

/// <summary>Static formatting helpers used from x:Bind function bindings.</summary>
public static class Fmt
{
    private static readonly CultureInfo De = new("de-DE");

    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        name = name.Trim().Trim('"', '\'');
        if (name.Contains('@')) name = name[..name.IndexOf('@')];

        var parts = name.Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length >= 2)
            return $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[^1][0])}";
        return char.ToUpper(parts[0][0]).ToString();
    }

    public static string ListDate(DateTime dt)
    {
        var now = DateTime.Now;
        if (dt.Date == now.Date) return dt.ToString("HH:mm", De);
        if (dt.Date == now.Date.AddDays(-1)) return "Gestern";
        if ((now.Date - dt.Date).TotalDays < 7) return dt.ToString("dddd", De);
        if (dt.Year == now.Year) return dt.ToString("d. MMM", De);
        return dt.ToString("dd.MM.yy", De);
    }

    public static string LongDate(DateTime dt) =>
        dt.ToString("dddd, d. MMMM yyyy 'um' HH:mm", De);

    public static Visibility Vis(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility VisNot(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Windows.UI.Text.FontWeight Weight(bool isRead) =>
        isRead ? FontWeights.Normal : FontWeights.SemiBold;

    public static double ReadOpacity(bool isRead) => isRead ? 0.72 : 1.0;

    public static string ReadIcon(bool isRead) => isRead ? "mail" : "mail-open";
    public static string ReadLabel(bool isRead) => isRead ? "Als ungelesen markieren" : "Als gelesen markieren";
    public static string FlagLabel(bool flagged) => flagged ? "Markierung entfernen" : "Markieren";
}
