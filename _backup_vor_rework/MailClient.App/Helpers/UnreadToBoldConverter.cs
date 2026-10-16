using Microsoft.UI.Xaml.Data;
using System;

namespace MailClient.App.Helpers;

public class UnreadToBoldConverter : IValueConverter
{
    // SemiBold = 600, Normal = 400
    private static readonly Windows.UI.Text.FontWeight SemiBold =
        new() { Weight = 600 };
    private static readonly Windows.UI.Text.FontWeight Normal =
        new() { Weight = 400 };

    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool isRead && !isRead ? SemiBold : Normal;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
