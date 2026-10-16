using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace MailClient.App.Helpers;

public static class ThemeHelper
{
    public static ElementTheme ToElementTheme(string theme) => theme switch
    {
        "Light" => ElementTheme.Light,
        "Dark" => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    public static void Apply(string theme)
    {
        var window = App.MainWindowInstance;
        if (window.Content is FrameworkElement root)
            root.RequestedTheme = ToElementTheme(theme);

        // Caption buttons (minimize/maximize/close) follow the selected theme.
        window.AppWindow.TitleBar.PreferredTheme = theme switch
        {
            "Light" => TitleBarTheme.Light,
            "Dark" => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode
        };
    }
}
