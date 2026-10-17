using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI;

namespace MailClient.App.Helpers;

/// <summary>Typed access to Themes/Tokens.xaml from code.</summary>
public static class Tokens
{
    // The indexer also searches merged dictionaries (Tokens.xaml), TryGetValue may not.
    private static object? Get(string key)
    {
        try { return Application.Current.Resources[key]; }
        catch { return null; }
    }

    public static double Double(string key, double fallback = 0) => Get(key) is double d ? d : fallback;

    public static TimeSpan Ms(string key) => TimeSpan.FromMilliseconds(Double(key));

    public static Color Color(string key, string fallbackKey = "TextPrimaryColor") =>
        Get(key) is Color c ? c : Get(fallbackKey) is Color f ? f : Microsoft.UI.Colors.White;

    public static Brush Brush(string key) => (Brush)Get(key)!;

    /// <summary>CSS hex (#RRGGBB or rgba()) for the mail WebView.</summary>
    public static string Css(string key)
    {
        var c = Color(key);
        return c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"rgba({c.R},{c.G},{c.B},{(c.A / 255.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)})";
    }
}
