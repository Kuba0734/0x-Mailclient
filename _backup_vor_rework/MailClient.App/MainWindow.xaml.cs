using Microsoft.UI.Xaml;
using MailClient.App.Views;

namespace MailClient.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);

        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
        AppWindow.Title = "Mail";

        RootFrame.Navigate(typeof(MainPage));
    }
}
