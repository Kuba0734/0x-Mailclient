using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Storage.Streams;
using Windows.UI;

namespace MailClient.App.Helpers;

/// <summary>
/// Inline SVG icons. Usage: <c>&lt;ImageIcon helpers:Svg.Kind="mail" helpers:Svg.Tone="Secondary" /&gt;</c>.
/// ImageIcon is an IconElement, so the same icons work in buttons, menus, settings cards and search boxes.
/// </summary>
public static class Svg
{
    // 24×24 stroke icons (stroke-width 1.75, round caps/joins).
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["mail"] = "M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z M22 6l-10 7L2 6",
        ["inbox"] = "M22 12h-6l-2 3h-4l-2-3H2 M5.45 5.11L2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z",
        ["send"] = "M22 2L11 13 M22 2l-7 20-4-9-9-4 20-7z",
        ["draft"] = "M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6 M16 13H8 M16 17H8 M10 9H8",
        ["archive"] = "M21 8v13H3V8 M1 3h22v5H1z M10 12h4",
        ["spam"] = "M7.86 2h8.28L22 7.86v8.28L16.14 22H7.86L2 16.14V7.86z M12 8v4 M12 16h.01",
        ["trash"] = "M3 6h18 M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2 M10 11v6 M14 11v6",
        ["compose"] = "M12 20h9 M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4z",
        ["edit"] = "M17 3a2.828 2.828 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5z",
        ["settings"] = "M4 21v-7 M4 10V3 M12 21v-9 M12 8V3 M20 21v-5 M20 12V3 M1 14h6 M9 8h6 M17 16h6",
        ["chevron-down"] = "M6 9l6 6 6-6",
        ["chevron-up"] = "M18 15l-6-6-6 6",
        ["chevron-right"] = "M9 18l6-6-6-6",
        ["more"] = "M12 11a1 1 0 1 1 0 2a1 1 0 1 1 0-2z M5 11a1 1 0 1 1 0 2a1 1 0 1 1 0-2z M19 11a1 1 0 1 1 0 2a1 1 0 1 1 0-2z",
        ["refresh"] = "M23 4v6h-6 M1 20v-6h6 M3.51 9a9 9 0 0 1 14.85-3.36L23 10 M1 14l4.64 4.36A9 9 0 0 0 20.49 15",
        ["check"] = "M20 6L9 17l-5-5",
        ["close"] = "M18 6L6 18 M6 6l12 12",
        ["plus"] = "M12 5v14 M5 12h14",
        ["minus"] = "M5 12h14",
        ["reply"] = "M9 14L4 9l5-5 M20 20v-7a4 4 0 0 0-4-4H4",
        ["reply-all"] = "M7 17l-5-5 5-5 M12 17l-5-5 5-5 M22 19v-3a4 4 0 0 0-4-4H7",
        ["forward"] = "M15 14l5-5-5-5 M4 20v-7a4 4 0 0 1 4-4h12",
        ["flag"] = "M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z M4 22v-7",
        ["paperclip"] = "M21.44 11.05l-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48",
        ["save"] = "M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z M17 21v-8H7v8 M7 3v5h8",
        ["download"] = "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M7 10l5 5 5-5 M12 15V3",
        ["external"] = "M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6 M15 3h6v6 M10 14L21 3",
        ["search"] = "M11 3a8 8 0 1 0 0 16a8 8 0 1 0 0-16z M21 21l-4.35-4.35",
        ["lock"] = "M5 11h14a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2z M7 11V7a5 5 0 0 1 10 0v4",
        ["offline"] = "M1 1l22 22 M16.72 11.06A10.94 10.94 0 0 1 19 12.55 M5 12.55a10.94 10.94 0 0 1 5.17-2.39 M10.71 5.05A16 16 0 0 1 22.58 9 M1.42 9a15.91 15.91 0 0 1 4.7-2.88 M8.53 16.11a6 6 0 0 1 6.95 0 M12 20h.01",
        ["alert"] = "M12 2a10 10 0 1 0 0 20a10 10 0 1 0 0-20z M12 8v4 M12 16h.01",
        ["success"] = "M22 11.08V12a10 10 0 1 1-5.93-9.14 M22 4L12 14.01l-3-3",
        ["folder"] = "M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z",
        ["user-plus"] = "M16 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2 M8.5 3a4 4 0 1 0 0 8a4 4 0 1 0 0-8z M20 8v6 M23 11h-6",
        ["power"] = "M18.36 6.64a9 9 0 1 1-12.73 0 M12 2v10",
        ["clock"] = "M12 2a10 10 0 1 0 0 20a10 10 0 1 0 0-20z M12 6v6l4 2",
        ["bell"] = "M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9 M13.73 21a2 2 0 0 1-3.46 0",
        ["motion"] = "M13 2L3 14h9l-1 8 10-12h-9l1-8z",
        ["info"] = "M12 2a10 10 0 1 0 0 20a10 10 0 1 0 0-20z M12 16v-4 M12 8h.01",
        ["server"] = "M4 2h16a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2z M4 14h16a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2z M6 6h.01 M6 18h.01",
        ["arrow-down"] = "M12 5v14 M19 12l-7 7-7-7",
        ["shield-check"] = "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z M9 12l2 2 4-4",
        ["mail-open"] = "M21.2 8.4c.5.38.8.97.8 1.6v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V10a2 2 0 0 1 .8-1.6l8-6a2 2 0 0 1 2.4 0z M22 10l-8.97 5.7a1.94 1.94 0 0 1-2.06 0L2 10",
        ["dot"] = "M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8z",
    };

    public static bool Exists(string kind) => Paths.ContainsKey(kind);

    // ── Attached properties ──────────────────────────────────────────────

    public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
        "Kind", typeof(string), typeof(Svg), new PropertyMetadata(null, OnChanged));

    public static string GetKind(DependencyObject o) => (string)o.GetValue(KindProperty);
    public static void SetKind(DependencyObject o, string value) => o.SetValue(KindProperty, value);

    /// <summary>Primary | Secondary | Tertiary | OnAccent | Danger | Success</summary>
    public static readonly DependencyProperty ToneProperty = DependencyProperty.RegisterAttached(
        "Tone", typeof(string), typeof(Svg), new PropertyMetadata("Primary", OnChanged));

    public static string GetTone(DependencyObject o) => (string)o.GetValue(ToneProperty);
    public static void SetTone(DependencyObject o, string value) => o.SetValue(ToneProperty, value);

    /// <summary>Sets kind and tone in one call (code-behind).</summary>
    public static void Set(ImageIcon icon, string kind, string? tone = null)
    {
        if (tone != null) SetTone(icon, tone);
        SetKind(icon, kind);
    }

    private static async void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ImageIcon icon) return;
        var kind = GetKind(icon);
        if (string.IsNullOrEmpty(kind) || !Paths.TryGetValue(kind, out var path))
        {
            icon.Source = null;
            return;
        }

        var markup = ToSvg(path, Tokens.Color(GetTone(icon) + "Color", "TextPrimaryColor"));
        var source = new SvgImageSource();
        icon.Source = source;

        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(markup).AsBuffer());
        stream.Seek(0);
        await source.SetSourceAsync(stream);
    }

    private static string ToSvg(string path, Color c) =>
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" " +
        $"stroke=\"#{c.R:X2}{c.G:X2}{c.B:X2}\" stroke-opacity=\"{(c.A / 255.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}\" stroke-width=\"1.75\" " +
        $"stroke-linecap=\"round\" stroke-linejoin=\"round\"><path d=\"{path}\"/></svg>";
}
