using System;
using System.Globalization;
using System.Windows.Data;
using PressViewer.Services;

namespace PressViewer.Converters;

/// <summary>把数字按全局千分位设置转成文本。</summary>
public class ThousandsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            int i => NumberFormatService.Format(i),
            long l => NumberFormatService.Format(l),
            _ => value?.ToString() ?? string.Empty,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}
