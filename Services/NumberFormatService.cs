using System.Globalization;

namespace PressViewer.Services;

/// <summary>
/// 数字显示格式：从 v1.1.1 起统一为带千分位的完整数字（1,145）。
///
/// 以前的两个开关（千分位、小按键 K/M 缩写）已经取消：完整数字是唯一的显示方式，
/// 图形里放不下时由控件自己缩字号 / 折行，而不是把数字缩写掉。
/// </summary>
public static class NumberFormatService
{
    public static string Format(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    public static string Format(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
