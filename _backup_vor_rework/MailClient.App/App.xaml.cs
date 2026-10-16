using MailClient.App.Helpers;
using Microsoft.UI.Xaml;
using System;
using System.IO;

namespace MailClient.App;

public partial class App : Application
{
    public static AccountService Accounts { get; private set; } = null!;
    public static string DataDir { get; private set; } = "";

    private Window? _window;

    public App()
    {
        InitializeComponent();

        try
        {
            DataDir = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch
        {
            DataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MailClient");
            Directory.CreateDirectory(DataDir);
        }

        Accounts = new AccountService(DataDir);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    public static MainWindow MainWindowInstance =>
        (MainWindow)((App)Current)._window!;
}
