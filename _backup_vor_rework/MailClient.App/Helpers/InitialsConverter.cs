using Microsoft.UI.Xaml.Data;
using System;

namespace MailClient.App.Helpers;

public class InitialsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string name || string.IsNullOrWhiteSpace(name))
            return "?";

        var parts = name.Trim().Split(' ',
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2)
            return $"{parts[0][0]}{parts[^1][0]}".ToUpper();

        return name.Trim()[0].ToString().ToUpper();
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
