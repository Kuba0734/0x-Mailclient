using Microsoft.UI.Xaml.Data;
using System;

namespace MailClient.App.Helpers;

public class DateTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not DateTime dt) return "";

        var now = DateTime.Now;
        var diff = now - dt;

        if (dt.Date == now.Date)
            return dt.ToString("HH:mm");

        if (diff.TotalDays < 7)
            return dt.ToString("ddd");

        if (dt.Year == now.Year)
            return dt.ToString("dd. MMM");

        return dt.ToString("dd.MM.yy");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
