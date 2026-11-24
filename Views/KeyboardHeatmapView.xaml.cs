using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PressViewer.Services;
using PressViewer.ViewModels;
// 项目同时启用了 WinForms，消除同名类型歧义（UserControl / Color / 对齐枚举）。
using UserControl = System.Windows.Controls.UserControl;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using Point = System.Windows.Point;
using TextAlignment = System.Windows.TextAlignment;

namespace PressViewer.Views;

/// <summary>
/// 键盘热力图：104 键美式布局，每个键帽显示累计次数，颜色按相对次数映射（对数刻度）。
/// 数据每 500ms 从 KeyCounterViewModel.KeyCounts 重新读取一次。
/// </summary>
public partial class KeyboardHeatmapView : UserControl
{
    // 1u = 48px，与 Canvas 尺寸配套；整块由 LayoutTransform 等比缩放并居中。
    private const double U = 48;
    private const double Pad = 0.05; // 键两侧各内缩 0.05u，键与键之间留出清晰的缝隙
    private const double MaxScale = 1.35; // 最大化时允许放大，但不超过这个倍数，否则键帽会糊
    private const int Columns = 22;

    private readonly List<Cap> _caps = new();
    private DispatcherTimer? _timer;

    /// <summary>外部提供的按键计数集合；为空时退回 DataContext 的 KeyCounterViewModel.KeyCounts。</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable<KeyCountItem>),
        typeof(KeyboardHeatmapView),
        new PropertyMetadata(null, (d, _) => ((KeyboardHeatmapView)d).Refresh())
    );

    public IEnumerable<KeyCountItem>? ItemsSource
    {
        get => (IEnumerable<KeyCountItem>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public KeyboardHeatmapView()
    {
        InitializeComponent();
        BuildKeyboard();
        BoardHost.SizeChanged += (_, _) => FitBoard();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>把 1098×311 的固定画布等比缩放到宿主区域；限制最大缩放并居中。</summary>
    private void FitBoard()
    {
        if (BoardHost.ActualWidth <= 0 || BoardHost.ActualHeight <= 0)
            return;

        var scale = Math.Min(BoardHost.ActualWidth / 1098.0, BoardHost.ActualHeight / 311.0);
        if (scale <= 0)
            return;

        scale = Math.Min(scale, MaxScale);
        BoardCanvas.LayoutTransform = new ScaleTransform(scale, scale);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeService.Changed += OnAppearanceChanged;
        NumberFormatService.Changed += OnAppearanceChanged;
        _timer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
        UpdateLegend();
        Refresh();

        // 初次布局时宽度可能还没定下来，等布局稳定后再适配一次。
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                FitBoard();
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(FitBoard));
            })
        );
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timer?.Stop();
        ThemeService.Changed -= OnAppearanceChanged;
        NumberFormatService.Changed -= OnAppearanceChanged;
    }

    private void OnAppearanceChanged()
    {
        UpdateLegend();
        Refresh();
    }

    /// <summary>图例跟随主题：空闲 → 中温 → 高温。</summary>
    private void UpdateLegend()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
        };
        brush.GradientStops.Add(new GradientStop(ThemeService.HeatIdle, 0));
        var stops = ThemeService.HeatStops;
        for (int i = 0; i < stops.Count; i++)
        {
            double offset = 0.12 + 0.88 * i / Math.Max(1, stops.Count - 1);
            brush.GradientStops.Add(new GradientStop(stops[i], offset));
        }
        LegendBar.Background = brush;
    }

    private void OnTick(object? sender, EventArgs e) => Refresh();

    private static Color CapIdle => ThemeService.HeatIdle;

    private static Color CapMid => ThemeService.HeatMid;

    private static Color CapHigh => ThemeService.HeatHot;

    private static Color BorderIdle => ThemeService.Blend(ThemeService.HeatIdle, ThemeService.MutedInk, 0.35);

    /// <summary>实际键名（GetKeyNameText 的返回值）→ 键帽 id。左右修饰键分开记。</summary>
    private static readonly Dictionary<string, string> Alias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Left Windows"] = "LWin",
        ["Right Windows"] = "RWin",
        ["Windows"] = "LWin",
        ["Shift"] = "LShift",
        ["Right Shift"] = "RShift",
        ["Ctrl"] = "LCtrl",
        ["Right Ctrl"] = "RCtrl",
        ["Control"] = "LCtrl",
        ["Alt"] = "LAlt",
        ["Right Alt"] = "RAlt",
        ["Caps Lock"] = "Caps",
        ["Escape"] = "Esc",
        ["Print Screen"] = "PrtSc",
        ["Scroll Lock"] = "ScrLk",
        ["Insert"] = "Ins",
        ["Delete"] = "Del",
        ["Page Up"] = "PgUp",
        ["Page Down"] = "PgDn",
        ["Num Lock"] = "Num",
        ["Up"] = "↑",
        ["Down"] = "↓",
        ["Left"] = "←",
        ["Right"] = "→",
        ["上"] = "↑",
        ["下"] = "↓",
        ["左"] = "←",
        ["右"] = "→",
    };

    private static (Color background, Color foreground) Heat(double t) =>
        ThemeService.HeatFromT(t);

    private void Refresh()
    {
        var source = ItemsSource ?? (DataContext as KeyCounterViewModel)?.KeyCounts;
        if (source is null)
            return;

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in source)
        {
            if (string.IsNullOrWhiteSpace(item.Key))
                continue;
            string id = Alias.TryGetValue(item.Key, out var mapped) ? mapped : item.Key;
            counts[id] = counts.GetValueOrDefault(id) + item.Count;
        }

        var positive = counts.Values.Where(v => v > 0).ToArray();
        int max = positive.Length == 0 ? 1 : positive.Max();
        int min = positive.Length == 0 ? 1 : positive.Min();

        foreach (var cap in _caps)
        {
            // Fn 没有独立的扫描码，键盘钩子拿不到，固定 0 次并隐藏数字
            bool isFn = string.Equals(cap.Def.Id, "Fn", StringComparison.Ordinal);
            int count = isFn ? 0 : counts.GetValueOrDefault(cap.Def.Id, 0);
            double t = count <= 0 ? 0 : ThemeService.HeatT(count, min, max);
            var (bg, fg) = count <= 0
                ? (ThemeService.HeatIdle, ThemeService.MutedInk)
                : Heat(t);

            cap.Border.Background = new SolidColorBrush(bg);
            cap.Border.BorderBrush = new SolidColorBrush(
                count > 0 ? ThemeService.Blend(BorderIdle, bg, 0.55) : BorderIdle
            );
            cap.NameText.Foreground = new SolidColorBrush(fg);
            cap.SubText.Foreground = new SolidColorBrush(fg) { Opacity = 0.6 };
            cap.CountText.Foreground = new SolidColorBrush(fg) { Opacity = count > 0 ? 1 : 0.35 };
            cap.CountText.Text = NumberFormatService.Format(count);
            cap.CountText.Visibility = isFn ? Visibility.Collapsed : Visibility.Visible;
        }

        int total = counts.Values.Sum();
        SummaryText.Text =
            $"总计 {NumberFormatService.Format(total)} 次 · 已覆盖 {counts.Count(kv => kv.Value > 0)} 个键";
    }

    private void BuildKeyboard()
    {
        foreach (var def in Layout)
        {
            double width = (def.Wide - Pad * 2) * U;
            double height = (def.RowSpan == 2 ? 1.9 : 0.9) * U;

            var name = new TextBlock
            {
                Text = def.Label,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                FontWeight = FontWeights.SemiBold,
                FontSize = def.Label.Length <= 1 ? U * 0.33 : def.Label.Length <= 3 ? U * 0.25 : U * 0.2,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var sub = new TextBlock
            {
                Text = def.Sub ?? string.Empty,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 3, 5, 0),
                FontSize = 10,
                Visibility = def.Sub is null ? Visibility.Collapsed : Visibility.Visible,
            };
            var countText = new TextBlock
            {
                Text = "0",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 5, 2),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
            };

            var content = new Grid();
            content.Children.Add(name);
            content.Children.Add(sub);
            content.Children.Add(countText);

            var border = new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(U * 0.1),
                Background = new SolidColorBrush(CapIdle),
                BorderBrush = new SolidColorBrush(BorderIdle),
                BorderThickness = new Thickness(1, 1, 1, 2),
                Child = content,
            };

            Canvas.SetLeft(border, (def.Col + Pad) * U);
            Canvas.SetTop(border, def.Row * U);
            BoardCanvas.Children.Add(border);

            _caps.Add(new Cap(def, border, name, sub, countText));
        }
    }

    private sealed record Cap(
        KeyCapDef Def,
        Border Border,
        TextBlock NameText,
        TextBlock SubText,
        TextBlock CountText
    );

    private sealed record KeyCapDef(
        string Id,
        string Label,
        string? Sub,
        double Row,
        double Col,
        double Wide,
        int RowSpan = 1
    );

    /// <summary>104 键美式布局，列位取自 designs/keyboard 的实测数据；1u = 48px。</summary>
    private static readonly KeyCapDef[] Layout =
    [
        // 功能键行
        new("Esc", "Esc", null, 0, 0, 1),
        new("F1", "F1", null, 0, 2, 1),
        new("F2", "F2", null, 0, 3, 1),
        new("F3", "F3", null, 0, 4, 1),
        new("F4", "F4", null, 0, 5, 1),
        new("F5", "F5", null, 0, 6.5, 1),
        new("F6", "F6", null, 0, 7.5, 1),
        new("F7", "F7", null, 0, 8.5, 1),
        new("F8", "F8", null, 0, 9.5, 1),
        new("F9", "F9", null, 0, 11, 1),
        new("F10", "F10", null, 0, 12, 1),
        new("F11", "F11", null, 0, 13, 1),
        new("F12", "F12", null, 0, 14, 1),
        new("PrtSc", "PrtSc", null, 0, 15.2, 1),
        new("ScrLk", "ScrLk", null, 0, 16.2, 1),
        new("Pause", "Pause", null, 0, 17.2, 1),
        // 数字行
        new("`", "`", "~", 1, 0, 1),
        new("1", "1", "!", 1, 1, 1),
        new("2", "2", "@", 1, 2, 1),
        new("3", "3", "#", 1, 3, 1),
        new("4", "4", "$", 1, 4, 1),
        new("5", "5", "%", 1, 5, 1),
        new("6", "6", "^", 1, 6, 1),
        new("7", "7", "&", 1, 7, 1),
        new("8", "8", "*", 1, 8, 1),
        new("9", "9", "(", 1, 9, 1),
        new("0", "0", ")", 1, 10, 1),
        new("-", "-", "_", 1, 11, 1),
        new("=", "=", "+", 1, 12, 1),
        new("Backspace", "Backspace", null, 1, 13, 2),
        new("Ins", "Ins", null, 1, 15.2, 1),
        new("Home", "Home", null, 1, 16.2, 1),
        new("PgUp", "PgUp", null, 1, 17.2, 1),
        new("Num", "Num", null, 1, 18.4, 1),
        new("Num/", "/", null, 1, 19.4, 1),
        new("Num*", "*", null, 1, 20.4, 1),
        new("Num-", "-", null, 1, 21.4, 1),
        // Tab 行
        new("Tab", "Tab", null, 2, 0, 1.5),
        new("Q", "Q", null, 2, 1.5, 1),
        new("W", "W", null, 2, 2.5, 1),
        new("E", "E", null, 2, 3.5, 1),
        new("R", "R", null, 2, 4.5, 1),
        new("T", "T", null, 2, 5.5, 1),
        new("Y", "Y", null, 2, 6.5, 1),
        new("U", "U", null, 2, 7.5, 1),
        new("I", "I", null, 2, 8.5, 1),
        new("O", "O", null, 2, 9.5, 1),
        new("P", "P", null, 2, 10.5, 1),
        new("[", "[", "{", 2, 11.5, 1),
        new("]", "]", "}", 2, 12.5, 1),
        new("\\", "\\", "|", 2, 13.5, 1.5),
        new("Del", "Del", null, 2, 15.2, 1),
        new("End", "End", null, 2, 16.2, 1),
        new("PgDn", "PgDn", null, 2, 17.2, 1),
        new("Num7", "7", null, 2, 18.4, 1),
        new("Num8", "8", null, 2, 19.4, 1),
        new("Num9", "9", null, 2, 20.4, 1),
        new("Num+", "+", null, 2, 21.4, 1, 2),
        // Caps 行
        new("Caps", "Caps", null, 3, 0, 1.75),
        new("A", "A", null, 3, 1.75, 1),
        new("S", "S", null, 3, 2.75, 1),
        new("D", "D", null, 3, 3.75, 1),
        new("F", "F", null, 3, 4.75, 1),
        new("G", "G", null, 3, 5.75, 1),
        new("H", "H", null, 3, 6.75, 1),
        new("J", "J", null, 3, 7.75, 1),
        new("K", "K", null, 3, 8.75, 1),
        new("L", "L", null, 3, 9.75, 1),
        new(";", ";", ":", 3, 10.75, 1),
        new("'", "'", "\"", 3, 11.75, 1),
        new("Enter", "Enter", null, 3, 12.75, 2.25),
        new("Num4", "4", null, 3, 18.4, 1),
        new("Num5", "5", null, 3, 19.4, 1),
        new("Num6", "6", null, 3, 20.4, 1),
        // Shift 行
        new("LShift", "Shift", null, 4, 0, 2.25),
        new("Z", "Z", null, 4, 2.25, 1),
        new("X", "X", null, 4, 3.25, 1),
        new("C", "C", null, 4, 4.25, 1),
        new("V", "V", null, 4, 5.25, 1),
        new("B", "B", null, 4, 6.25, 1),
        new("N", "N", null, 4, 7.25, 1),
        new("M", "M", null, 4, 8.25, 1),
        new(",", ",", "<", 4, 9.25, 1),
        new(".", ".", ">", 4, 10.25, 1),
        new("/", "/", "?", 4, 11.25, 1),
        new("RShift", "Shift", null, 4, 12.25, 2.75),
        new("↑", "↑", null, 4, 16.2, 1),
        new("Num1", "1", null, 4, 18.4, 1),
        new("Num2", "2", null, 4, 19.4, 1),
        new("Num3", "3", null, 4, 20.4, 1),
        new("NumEnter", "Enter", null, 4, 21.4, 1, 2),
        // 底行
        new("LCtrl", "Ctrl", null, 5, 0, 1.25),
        new("LWin", "Win", null, 5, 1.25, 1.25),
        new("LAlt", "Alt", null, 5, 2.5, 1.25),
        new("Space", "Space", null, 5, 3.75, 6.25),
        new("RAlt", "Alt", null, 5, 10, 1.25),
        new("RWin", "Win", null, 5, 11.25, 1.25),
        new("Fn", "Fn", null, 5, 12.5, 1.25),
        new("RCtrl", "Ctrl", null, 5, 13.75, 1.25),
        new("←", "←", null, 5, 15.2, 1),
        new("↓", "↓", null, 5, 16.2, 1),
        new("→", "→", null, 5, 17.2, 1),
        new("Num0", "0", null, 5, 18.4, 2),
        new("Num.", ".", null, 5, 20.4, 1),
    ];
}
