using System.Globalization;
using System.Windows.Data;
using Binding = System.Windows.Data.Binding;
using PressViewer.Models;

namespace PressViewer.Converters;

/// <summary>把手柄按键 id（DPadUp、LStick…）显示成中文。</summary>
public class GamepadButtonNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        GamepadButtonIds.Display(value as string ?? string.Empty);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
