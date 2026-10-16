using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.Foundation;
using Windows.UI;

namespace MailClient.App.Helpers;

/// <summary>Static formatting helpers used from x:Bind function bindings.</summary>
public static class Fmt
{
    private static readonly CultureInfo De = new("de-DE");

    private static readonly (Color A, Color B)[] AvatarPalette =
    [
        (Color.FromArgb(255, 99, 102, 241), Color.FromArgb(255, 139, 92, 246)),
        (Color.FromArgb(255, 236, 72, 153), Color.FromArgb(255, 244, 63, 94)),
        (Color.FromArgb(255, 249, 115, 22), Color.FromArgb(255, 234, 179, 8)),
        (Color.FromArgb(255, 16, 185, 129), Color.FromArgb(255, 20, 184, 166)),
        (Color.FromArgb(255, 14, 165, 233), Color.FromArgb(255, 59, 130, 246)),
        (Color.FromArgb(255, 168, 85, 247), Color.FromArgb(255, 217, 70, 239)),
        (Color.FromArgb(255, 239, 68, 68), Color.FromArgb(255, 249, 115, 22)),
        (Color.FromArgb(255, 34, 197, 94), Color.FromArgb(255, 132, 204, 22)),
    ];

    private static readonly Dictionary<int, Brush> BrushCache = [];

    public static Brush AvatarBrush(string? key)
    {
        var idx = StableHash(key ?? "") % AvatarPalette.Length;
        if (BrushCache.TryGetValue(idx, out var cached)) return cached;

        var (a, b) = AvatarPalette[idx];
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = a, Offset = 0 },
                new GradientStop { Color = b, Offset = 1 }
            }
        };
        BrushCache[idx] = brush;
        return brush;
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            var h = 23;
            foreach (var ch in s.ToLowerInvariant()) h = h * 31 + ch;
            return Math.Abs(h % 10007);
        }
    }

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

    public static string ReadGlyph(bool isRead) => isRead ? "" : "";
    public static string ReadLabel(bool isRead) => isRead ? "Als ungelesen markieren" : "Als gelesen markieren";
    public static string FlagLabel(bool flagged) => flagged ? "Markierung entfernen" : "Markieren";
}
