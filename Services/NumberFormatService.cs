using System;
using System.Globalization;

namespace PressViewer.Services;

/// <summary>
/// 数字显示格式：是否使用千分位（1,145）。
/// 全局一份，设置页改了立刻通知各页面重绑。
/// </summary>
public static class NumberFormatService
{
    public static bool UseSeparator { get; private set; } = true;

    /// <summary>小面积按键（滚轮、肩键、ABXY 等）是否用 K/M 缩写。</summary>
    public static bool CompactSmall { get; private set; } = true;

    /// <summary>格式变化时触发；页面 VM 收到后通知所有数字属性重新求值。</summary>
    public static event Action? Changed;

    public static void Set(bool useSeparator)
    {
        if (UseSeparator == useSeparator)
            return;

        UseSeparator = useSeparator;
        Changed?.Invoke();
    }

    public static void SetCompactSmall(bool compact)
    {
        if (CompactSmall == compact)
            return;

        CompactSmall = compact;
        Changed?.Invoke();
    }

    public static string Format(int value) =>
        UseSeparator ? value.ToString("N0", CultureInfo.CurrentCulture) : value.ToString(CultureInfo.CurrentCulture);

    public static string Format(long value) =>
        UseSeparator ? value.ToString("N0", CultureInfo.CurrentCulture) : value.ToString(CultureInfo.CurrentCulture);

    /// <summary>小面积按键专用：开启开关且超过 999 时缩写为 1.2K / 1.3M。</summary>
    public static string FormatCompact(int value)
    {
        if (!CompactSmall || value < 1000)
            return value.ToString("0");

        if (value < 1_000_000)
            return (value / 1000.0).ToString("0.#", CultureInfo.CurrentCulture) + "K";

        return (value / 1_000_000.0).ToString("0.#", CultureInfo.CurrentCulture) + "M";
    }
}
