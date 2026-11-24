using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Colors = System.Windows.Media.Colors;
using ResourceDictionary = System.Windows.ResourceDictionary;

namespace PressViewer.Services;

/// <summary>一套主题：UI 强调色 + 独立热力色标（低→高，可多段跨色相）。</summary>
public sealed record ThemePalette(
    string Name,
    string DisplayName,
    Color Accent,
    Color Deep,
    Color[] HeatStops,
    Color[]? DarkHeatStops = null
);

/// <summary>
/// 全局主题：主题色 + 深色模式。
///
/// 所有界面颜色都来自 <c>Resources/Theme.xaml</c> 的 *Color 键，
/// 这里只改 Application.Resources 里的 Color，SolidColorBrush 通过
/// DynamicResource 绑着它，所以切换即时生效，不用重建窗口。
/// </summary>
public static class ThemeService
{
    public static IReadOnlyList<ThemePalette> Palettes { get; } = new[]
    {
        new ThemePalette("Pink", "樱花轻语", C("#E8559A"), C("#C23A78"), new[] { C("#FFE3C8"), C("#F26AA8"), C("#8E1F6B") }),
        new ThemePalette("Aurora", "极光幻彩", C("#6C5CE7"), C("#3A2E9E"), new[] { C("#7BE0FF"), C("#B388FF"), C("#FF6FD8"), C("#6C5CE7") }),
        new ThemePalette("DeepSea", "深海蓝调", C("#1E88E5"), C("#0D47A1"), new[] { C("#9FE8F5"), C("#38A3E0"), C("#0B2E59") }),
        new ThemePalette("Sunset", "落日熔金", C("#FF7043"), C("#C0392B"), new[] { C("#FFE08A"), C("#FF8A3D"), C("#B3241C") }),
        new ThemePalette("Forest", "森林秘语", C("#2E9E5B"), C("#14532D"), new[] { C("#D8F3B8"), C("#4CAF50"), C("#0B3D2E") }),
        new ThemePalette("Champagne", "香槟鎏金", C("#C9A227"), C("#8A6D1D"), new[] { C("#FFF3C4"), C("#E6C65C"), C("#7A5A12") }),
        new ThemePalette("Lavender", "薰衣草梦境", C("#9C7BF0"), C("#5B3FA8"), new[] { C("#E9DCFF"), C("#A98BF5"), C("#4A2C8F") }),
        new ThemePalette("MintSoda", "薄荷汽水", C("#3ECFB2"), C("#12816E"), new[] { C("#D6FFF4"), C("#3ED8C0"), C("#0B6B5E") }),
        new ThemePalette("Cyan", "青碧如洗", C("#06B6D4"), C("#0E7490"), new[] { C("#CFF5FF"), C("#22B6D1"), C("#0A4E70") }),
        new ThemePalette("Coral", "珊瑚晚霞", C("#EF4444"), C("#B91C1C"), new[] { C("#FFD9A8"), C("#FF6B4A"), C("#8E0F2E") }),
        new ThemePalette("Graphite", "石墨夜色", C("#6B7280"), C("#374151"),
            new[] { C("#DDE3EC"), C("#7E8798"), C("#1F2530") },
            new[] { C("#39404D"), C("#8B94A3"), C("#C2C9D4") }),
        new ThemePalette("Rainbow", "霓虹虹光", C("#FF4D6D"), C("#B3122E"),
            new[] { C("#FF3B3B"), C("#FF3B3B"), C("#FF7A45"), C("#FFB347"), C("#FFE066"), C("#7CE38B"), C("#66E0F0"), C("#7C96FF"), C("#C9A7FF") },
            new[] { C("#FF6B6B"), C("#FF6B6B"), C("#FF8A5C"), C("#FFB866"), C("#FFE066"), C("#7CE38B"), C("#66E0F0"), C("#7C96FF"), C("#C9A7FF") }),
    };

    public static string CurrentTheme { get; private set; } = "Pink";

    public static bool IsDark { get; private set; }

    public static ThemePalette Current =>
        Palettes.FirstOrDefault(p => p.Name == CurrentTheme) ?? Palettes[0];

    public static Color Accent => Current.Accent;

    public static Color Deep => Current.Deep;

    /// <summary>键盘键帽 0 次时的底色：亮色纯白，深色用卡片色。</summary>
    public static Color Surface => IsDark ? C("#262A33") : Colors.White;

    /// <summary>热力图最低档颜色（0 次）。</summary>
    public static Color HeatIdle => IsDark ? C("#262A33") : Colors.White;

    private static Color CardSurface => IsDark ? C("#1B1D24") : Colors.White;

    /// <summary>
    /// 当前模式下实际使用的热力色标。默认深浅色一致；只有显式给了 DarkHeatStops 的主题（石墨）才单独换一套。
    /// </summary>
    public static IReadOnlyList<Color> HeatStops
    {
        get
        {
            var palette = Current;
            if (!IsDark)
                return palette.HeatStops;

            if (palette.DarkHeatStops is { Length: >= 2 } darkStops)
                return darkStops;

            return palette.HeatStops;
        }
    }

    public static Color HeatCool => HeatStops[0];

    public static Color HeatWarm => HeatStops[HeatStops.Count / 2];

    public static Color HeatHot => HeatStops[^1];

    /// <summary>兼容旧名：热力中档。</summary>
    public static Color HeatMid => HeatWarm;

    /// <summary>按归一化热度 t（0..1）在多段色标之间插值。</summary>
    public static (Color Background, Color Foreground) HeatFromT(double t)
    {
        var stops = HeatStops;
        double pos = Math.Clamp(t, 0, 1) * (stops.Count - 1);
        int index = Math.Min((int)pos, stops.Count - 2);
        double fraction = pos - index;
        var bg = Blend(stops[index], stops[index + 1], fraction);
        return (bg, HeatText(bg));
    }

    /// <summary>按背景亮度取对比色：深底用白字，亮底统一用深灰字（深色模式同理）。</summary>
    public static Color HeatText(Color background) =>
        Luminance(background) < 0.55 ? Colors.White : C("#2A2D36");

    /// <summary>次要文字（0 次键帽上的数字、图例说明）。</summary>
    public static Color MutedInk => IsDark ? C("#7C8292") : C("#8A90A0");

    /// <summary>
    /// 把次数映射到 0..1：以这批数据里最小的非零次数为起点、最大次数为终点，
    /// 这样无论最少按了几次，它都落在色标起点（红端）。
    /// </summary>
    public static double HeatT(int count, int min, int max)
    {
        if (count <= 0 || max <= 0)
            return 0;

        double lo = Math.Log(1 + Math.Max(0, min));
        double hi = Math.Log(1 + max);
        if (hi - lo < 1e-9)
            return 1;

        return Math.Clamp((Math.Log(1 + count) - lo) / (hi - lo), 0, 1);
    }

    /// <summary>
    /// 自绘图形通用的热度映射：0 次退回底色（不着色），其余按对数刻度在
    /// 色标之间过渡，文字自动取对比色。
    /// </summary>
    public static (Color Background, Color Foreground) Heat(int count, int max, int min = 1)
    {
        // 只有真的 0 次才不着色；最低的非零次数 t=0，取色标首色
        if (count <= 0)
            return (HeatIdle, MutedInk);

        return HeatFromT(HeatT(count, min, max));
    }

    /// <summary>主题变化时触发，自绘控件（键盘 / 鼠标 / 手柄）据此重绘。</summary>
    public static event Action? Changed;


    public static void Apply(string themeName, bool dark)
    {
        CurrentTheme = Palettes.Any(p => p.Name == themeName) ? themeName : "Pink";
        IsDark = dark;

        var res = Application.Current?.Resources;
        if (res is null)
            return;

        var accent = Current.Accent;
        var deep = Current.Deep;
        var card = dark ? C("#1B1D24") : Colors.White;
        var bg = dark ? C("#14151A") : C("#F5F6FA");
        var ink = dark ? C("#ECEFF5") : C("#23262F");
        var subInk = dark ? C("#B6BCC9") : C("#5A6070");
        var muted = dark ? C("#7C8292") : C("#8A90A0");
        var hair = dark ? C("#2A2D36") : C("#EFF1F6");
        var border = dark ? C("#2A2D36") : C("#E9EBF2");

        Set(res, "BgColor", bg);
        Set(res, "CardColor", card);
        Set(res, "CardBorderColor", border);
        Set(res, "InkColor", ink);
        Set(res, "SubInkColor", subInk);
        Set(res, "MutedColor", muted);
        Set(res, "HairColor", hair);
        Set(res, "HeaderBgColor", dark ? C("#20222A") : C("#F8F9FC"));
        Set(res, "AltRowColor", dark ? C("#1F2129") : C("#FBFCFE"));
        Set(res, "RowHoverColor", dark ? C("#262933") : Blend(accent, Colors.White, 0.94));
        Set(res, "NavHoverColor", dark ? C("#23262F") : C("#F5F6FA"));
        Set(res, "ShadowColor", dark ? Colors.Black : C("#2A2140"));

        Set(res, "AccentColor", accent);
        Set(res, "AccentDeepColor", deep);
        Set(res, "AccentSoftColor", dark ? Blend(accent, card, 0.80) : Blend(accent, Colors.White, 0.90));
        Set(res, "AccentSoftHoverColor", dark ? Blend(accent, card, 0.66) : Blend(accent, Colors.White, 0.78));
        Set(res, "AccentSoftBorderColor", dark ? Blend(accent, card, 0.60) : Blend(accent, Colors.White, 0.70));
        Set(res, "AccentHoverColor", dark ? Blend(accent, Colors.Black, 0.18) : Blend(accent, Colors.White, 0.28));
        Set(res, "AccentPressedColor", Blend(deep, Colors.Black, 0.22));

        // SolidColorBrush.Color 上的 DynamicResource 在 Freezable 上不会实时生效，
        // 这里直接把 Brush 资源换成新画刷，所有 DynamicResource 引用才会跟着变。
        SetBrush(res, "BgBrush", bg);
        SetBrush(res, "CardBrush", card);
        SetBrush(res, "CardBorderBrush", border);
        SetBrush(res, "InkBrush", ink);
        SetBrush(res, "SubInkBrush", subInk);
        SetBrush(res, "MutedBrush", muted);
        SetBrush(res, "HairBrush", hair);
        SetBrush(res, "HeaderBgBrush", GetColor(res, "HeaderBgColor"));
        SetBrush(res, "AltRowBrush", GetColor(res, "AltRowColor"));
        SetBrush(res, "RowHoverBrush", GetColor(res, "RowHoverColor"));
        SetBrush(res, "NavHoverBrush", GetColor(res, "NavHoverColor"));
        SetBrush(res, "AccentBrush", accent);
        SetBrush(res, "AccentDeepBrush", deep);
        SetBrush(res, "AccentSoftBrush", GetColor(res, "AccentSoftColor"));
        SetBrush(res, "AccentSoftHoverBrush", GetColor(res, "AccentSoftHoverColor"));
        SetBrush(res, "AccentSoftBorderBrush", GetColor(res, "AccentSoftBorderColor"));
        SetBrush(res, "AccentHoverBrush", GetColor(res, "AccentHoverColor"));
        SetBrush(res, "AccentPressedBrush", GetColor(res, "AccentPressedColor"));
        SetBrush(res, "DangerBrush", C("#E5484D"));
        SetBrush(res, "SuccessBrush", C("#2F9E44"));

        Changed?.Invoke();
    }

    private static Color GetColor(ResourceDictionary res, string key) =>
        res[key] is Color color ? color : Colors.Transparent;

    private static void SetBrush(ResourceDictionary res, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        res[key] = brush;
    }

    public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    public static Color Blend(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t)
        );
    }

    private static double Luminance(Color c)
    {
        double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static void Set(ResourceDictionary res, string key, Color color) => res[key] = color;
}
